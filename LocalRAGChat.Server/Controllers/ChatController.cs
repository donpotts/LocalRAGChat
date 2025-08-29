using LocalRAGChat.Server.Services;
using LocalRAGChat.Shared;
using Microsoft.AspNetCore.Mvc;
using OllamaSharp;

namespace LocalRAGChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly RagService _ragService;
    private readonly ILogger<ChatController> _logger;
    private readonly OllamaApiClient _ollamaClient;

    public ChatController(RagService ragService, ILogger<ChatController> logger, OllamaApiClient ollamaClient)
    {
        _ragService = ragService;
        _logger = logger;
        _ollamaClient = ollamaClient;
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetModels()
    {
        try
        {
            var models = await _ollamaClient.ListLocalModels();
            var modelNames = models.Select(m => m.Name).ToList();
            return Ok(modelNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve models from Ollama");
            // Fallback to hardcoded models if Ollama is not available
            var fallbackModels = new List<string> { "llama3:8b", "mistral", "phi3" };
            return Ok(fallbackModels);
        }
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.ModelId))
        {
            return BadRequest("Invalid request payload. DocumentId, Question, and ModelId are required.");
        }

        try
        {
            var answer = await _ragService.AskQuestionAsync(request.DocumentId, request.Question, request.ModelId);
            return Ok(new { Answer = answer });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the chat request for document {DocumentId}.", request.DocumentId);
            return StatusCode(500, "An internal server error occurred while contacting the AI model.");
        }
    }
}