#pragma warning disable SKEXP0050 // Suppress TextChunker warning

using LocalRAGChat.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.Text;
using System.Text.Json;
using UglyToad.PdfPig;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace LocalRAGChat.Server.Services;

public class RagService
{
    private readonly IDbContextFactory<RagDbContext> _dbContextFactory;
    private readonly OllamaApiClient _ollama;
    private readonly string _embeddingModelId;
    private readonly ILogger<RagService> _logger;
    private readonly ConcurrentDictionary<int, List<DocumentChunk>> _chunkCache = new();

    public RagService(IDbContextFactory<RagDbContext> dbContextFactory, IConfiguration config, ILogger<RagService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        var ollamaEndpoint = config["Ollama:Endpoint"] ?? throw new InvalidOperationException("Ollama endpoint not configured.");
        _embeddingModelId = config["Ollama:EmbeddingModelId"] ?? throw new InvalidOperationException("Ollama embedding model not configured.");
        _ollama = new OllamaApiClient(new Uri(ollamaEndpoint));
        _ = LoadAllChunksIntoCache();
    }

    private async Task LoadAllChunksIntoCache()
    {
        _logger.LogInformation("Starting to pre-load all document chunks into memory cache...");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var allDocuments = await dbContext.Documents.Include(d => d.Chunks).ToListAsync();

        _chunkCache.Clear();
        foreach (var doc in allDocuments)
        {
            foreach (var chunk in doc.Chunks)
            {
                chunk.Embedding = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson) ?? [];
            }
            _chunkCache[doc.Id] = doc.Chunks.ToList();
        }
        _logger.LogInformation("Finished loading {DocumentCount} documents into the cache.", allDocuments.Count);
    }

    private async Task<Document> IngestAndCacheDocumentAsync(string fileName, Stream stream)
    {
        var text = Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? string.Join("\n", PdfDocument.Open(stream).GetPages().Select(p => p.Text))
            : await new StreamReader(stream).ReadToEndAsync();
        var chunks = TextChunker.SplitPlainTextParagraphs([text], 1000, 100);
        var newDocument = new Document { FileName = fileName, UploadedAt = DateTime.UtcNow };

        foreach (var chunkText in chunks)
        {
            var request = new GenerateEmbeddingRequest { Model = _embeddingModelId, Prompt = chunkText };
            var embeddingsResponse = await _ollama.GenerateEmbeddings(request);
            newDocument.Chunks.Add(new DocumentChunk
            {
                Content = chunkText,
                EmbeddingJson = JsonSerializer.Serialize(embeddingsResponse.Embedding)
            });
        }
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        dbContext.Documents.Add(newDocument);
        await dbContext.SaveChangesAsync();
        foreach (var chunk in newDocument.Chunks)
        {
            chunk.Embedding = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson) ?? [];
        }
        _chunkCache[newDocument.Id] = newDocument.Chunks.ToList();

        return newDocument;
    }

    public async Task<Document> IngestFileAsync(string fileName, Stream stream)
    {
        return await IngestAndCacheDocumentAsync(fileName, stream);
    }

    public async Task<string> AskQuestionAsync(int documentId, string question, string modelId)
    {
        if (string.IsNullOrWhiteSpace(question))
            return "Question is empty.";

        if (!_chunkCache.TryGetValue(documentId, out var documentChunks) || documentChunks == null)
        {
            await LoadAllChunksIntoCache();
            if (!_chunkCache.TryGetValue(documentId, out documentChunks) || documentChunks == null)
                return "Could not find or load the specified document.";
        }

        // Generate embedding for the query
        var queryRequest = new GenerateEmbeddingRequest { Model = _embeddingModelId, Prompt = question };
        var queryEmbeddingResponse = await _ollama.GenerateEmbeddings(queryRequest);
        var queryEmbedding = queryEmbeddingResponse.Embedding.Select(e => (float)e).ToArray();

        // Rank chunks by cosine similarity
        var ranked = documentChunks
            .Select((chunk, idx) => new
            {
                Index = idx,
                chunk.Content,
                Similarity = VectorMath.CosineSimilarity(queryEmbedding, chunk.Embedding)
            })
            .OrderByDescending(x => x.Similarity)
            .Take(12) // initial candidate pool
            .ToList();

        if (!ranked.Any())
            return "No document content available to answer.";

        var topSim = ranked.First().Similarity;

        // Dynamic unrelated-question detection: if best similarity is very low, likely unrelated
        if (topSim < 0.18f)
        {
            return "I cannot answer that because it does not appear to relate to the uploaded document.";
        }

        // Form a coherent context window: include chunks close to top similarity or above a floor
        var contextSelection = ranked
            .Where(x => x.Similarity >= Math.Max(0.15f, topSim - 0.10f))
            .OrderByDescending(x => x.Similarity)
            .Take(8)
            .ToList();

        // If after filtering we lost everything, fall back to the single best chunk
        if (!contextSelection.Any())
        {
            var top = ranked.First();
            contextSelection = new() { top };
        }

        // Build numbered context for citation
        var contextBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < contextSelection.Count; i++)
        {
            var c = contextSelection[i];
            contextBuilder.AppendLine($"[C{i + 1}] (score {c.Similarity:F3})\n{c.Content.Trim()}\n---");
        }
        var contextText = contextBuilder.ToString();

        // Store available citation numbers for validation
        var availableCitations = Enumerable.Range(1, contextSelection.Count).Select(i => $"C{i}").ToHashSet();

        // Strict system instructions that REQUIRE citations
        var systemPrompt = @"You are a retrieval augmented assistant. Use ONLY the provided document context passages to answer the user's question.
                            CRITICAL RULES:
                            - You MUST cite every fact using the citation tags like [C1], [C2], [C3] etc.
                            - NEVER provide any answer without proper citations from the context passages below.
                            - Only use facts explicitly present in the context passages below.
                            - If the answer requires information not present in the context, reply exactly with: I cannot answer based on the provided document.
                            - Do not invent, guess, generalize from outside knowledge, or define general concepts unless they are directly stated.
                            - Every sentence or fact MUST have a citation like [C1] or [C2].

                            DOCUMENT CONTEXT PASSAGES:
                            {context}
                            USER QUESTION: {question}
                            
                            Remember: ALL facts must have citations like [C1], [C2]. If you cannot cite everything from the context, say 'I cannot answer based on the provided document.':";

        var prompt = systemPrompt.Replace("{context}", contextText).Replace("{question}", question.Trim());

        var chat = new Chat(_ollama, _ => { }) { Model = modelId };

        var conversationHistory = await chat.Send(prompt, CancellationToken.None);
        var raw = conversationHistory.LastOrDefault()?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return "No response from the AI model.";

        // STRICT citation validation - NO EXCEPTIONS
        var citationValidationResult = ValidateCitations(raw, availableCitations);
        
        if (!citationValidationResult.IsValid)
        {
            return "I cannot answer based on the provided document.";
        }

        // Additional check: If no citations found in a non-refusal response, reject it
        bool hasCitation = raw.Contains("[C", StringComparison.OrdinalIgnoreCase);
        if (!hasCitation && !raw.Contains("I cannot answer based on the provided document", StringComparison.OrdinalIgnoreCase))
        {
            return "I cannot answer based on the provided document.";
        }

        // If the model hallucinated generic phrasing but similarity was low-ish, replace with refusal.
        if (topSim < 0.20f && !raw.Contains("I cannot answer based on the provided document"))
        {
            return "I cannot answer based on the provided document.";
        }

        return raw;
    }

    private static (bool IsValid, List<string> Issues) ValidateCitations(string response, HashSet<string> availableCitations)
    {
        var issues = new List<string>();
        
        // Check if response is a refusal (which is always valid)
        if (response.Contains("I cannot answer based on the provided document", StringComparison.OrdinalIgnoreCase))
        {
            return (true, issues);
        }

        // Extract all citation references from the response
        var citationPattern = @"\[C(\d+)\]";
        var citationMatches = Regex.Matches(response, citationPattern);
        var citationsInResponse = citationMatches
            .Cast<Match>()
            .Select(m => $"C{m.Groups[1].Value}")
            .Distinct()
            .ToHashSet();

        // STRICT: If no citations, it's invalid (unless it's a refusal)
        if (!citationsInResponse.Any())
        {
            issues.Add("Response contains no citations");
            return (false, issues);
        }

        // Check if all cited references are available
        var invalidCitations = citationsInResponse.Except(availableCitations).ToList();
        if (invalidCitations.Any())
        {
            issues.Add($"Response references unavailable citations: {string.Join(", ", invalidCitations)}");
            return (false, issues);
        }

        // Check for obvious general knowledge that suggests the model is not using the document
        if (ContainsObviousGeneralKnowledge(response))
        {
            issues.Add("Response contains obvious general knowledge without proper document reference");
            return (false, issues);
        }

        return (true, issues);
    }

    private static bool ContainsObviousGeneralKnowledge(string response)
    {
        var obviousGeneralKnowledge = new[]
        {
            "it is well known", "everyone knows", "it's common knowledge",
            "wikipedia", "according to scientists", "research shows",
            "the capital of", "the president of", "world war",
            "the sky is blue", "water boils at", "gravity is",
            "generally speaking", "in general", "typically", "usually", "commonly",
            "it is known that", "as we know", 
            "studies show", "research indicates", "experts say", "scientists believe",
            "in most cases", "generally accepted", "widely understood", "commonly accepted"
        };

        var lowerResponse = response.ToLower();
        return obviousGeneralKnowledge.Any(indicator => lowerResponse.Contains(indicator));
    }

    private static bool IsGeneralKnowledgeQuestion(string question)
    {
        // Simplified - only catch very obvious general knowledge questions
        var obviouslyGeneral = new[]
        {
            "what is the capital of",
            "who is the president of",
            "when was world war",
            "what is 2+2",
            "how many days in a year",
            "what color is the sky",
            "what is gravity"
        };

        var lowerQuestion = question.ToLower();
        return obviouslyGeneral.Any(pattern => lowerQuestion.Contains(pattern));
    }

    private static bool ContainsDocumentReferences(string question)
    {
        var documentReferences = new[]
        {
            "document", "text", "paper", "article", "book", "file", "content",
            "according to", "based on", "in this", "the author", "mentioned",
            "states", "describes", "explains", "discusses", "chapter", "section"
        };

        return documentReferences.Any(reference => question.ToLower().Contains(reference));
    }

    public async Task<bool> DeleteDocumentAsync(int documentId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var document = await dbContext.Documents.FindAsync(documentId);
        if (document == null) return false;

        dbContext.Documents.Remove(document);
        await dbContext.SaveChangesAsync();

        _chunkCache.TryRemove(documentId, out _);
        return true;
    }
}