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
        if (!_chunkCache.TryGetValue(documentId, out var documentChunks) || documentChunks == null)
        {
            await LoadAllChunksIntoCache();
            if (!_chunkCache.TryGetValue(documentId, out documentChunks) || documentChunks == null)
                return "Could not find or load the specified document.";
        }

        var queryRequest = new GenerateEmbeddingRequest { Model = _embeddingModelId, Prompt = question };
        var queryEmbeddingResponse = await _ollama.GenerateEmbeddings(queryRequest);
        var queryEmbedding = queryEmbeddingResponse.Embedding.Select(e => (float)e).ToArray();

        var topChunks = documentChunks
            .Select(chunk => (chunk.Content, similarity: VectorMath.CosineSimilarity(queryEmbedding, chunk.Embedding)))
            .OrderByDescending(x => x.similarity)
            //.Take(6)
            .Where(x => x.similarity > 0.3)
            //.AsParallel()
            .Select(x => x.Content)
            .ToList();

        if (!topChunks.Any())
        {
            return "I don't have enough information from the document to answer that question.";
        }

        var context = string.Join("\n---\n", topChunks);
        var prompt = $"Based *only* on the following context, answer the user's question.\n\nContext:\n{context}\n\nQuestion: {question}\n\nAnswer:";
        var chat = new Chat(_ollama, _ => { });
        chat.Model = modelId;
        var conversationHistory = await chat.Send(prompt, CancellationToken.None);
        return conversationHistory.LastOrDefault()?.Content ?? "No response from the AI model.";
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