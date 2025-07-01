using LocalRAGChat.Server.Services;
using LocalRAGChat.Shared;
using Microsoft.AspNetCore.Mvc;

namespace LocalRAGChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly RagService _ragService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(RagService ragService, ILogger<ChatController> logger)
    {
        _ragService = ragService;
        _logger = logger;
    }

    [HttpGet("models")]
    public IActionResult GetModels()
    {
        var models = new List<string> { "llama3:8b", "mistral", "phi3" };
        return Ok(models);
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