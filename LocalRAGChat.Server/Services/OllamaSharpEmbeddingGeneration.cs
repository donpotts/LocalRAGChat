#pragma warning disable SKEXP0001
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using OllamaSharp;
using OllamaSharp.Models;

namespace LocalRAGChat.Server.Services;

public class OllamaSharpEmbeddingGeneration(OllamaApiClient client, string model) : ITextEmbeddingGenerationService
{
    private readonly OllamaApiClient _client = client;
    private readonly string _model = model;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IList<string> data, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var embeddings = new List<ReadOnlyMemory<float>>();
        foreach (var text in data)
        {
            var request = new GenerateEmbeddingRequest { Model = _model, Prompt = text };
            var response = await _client.GenerateEmbeddings(request, cancellationToken);
            embeddings.Add(new ReadOnlyMemory<float>(response.Embedding.Select(e => (float)e).ToArray()));
        }
        return embeddings;
    }
}