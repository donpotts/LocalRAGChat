using LocalRAGChat.Server.Data;
using LocalRAGChat.Server.Services;
using LocalRAGChat.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalRAGChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly RagService _ragService;
    private readonly IDbContextFactory<RagDbContext> _dbContextFactory;

    public DocumentsController(RagService ragService, IDbContextFactory<RagDbContext> dbContextFactory)
    {
        _ragService = ragService;
        _dbContextFactory = dbContextFactory;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentDto>>> GetDocuments()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.Documents
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentDto(d.Id, d.FileName, d.UploadedAt))
            .ToListAsync();
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadDocument(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

        await using var stream = file.OpenReadStream();

        var documentEntity = await _ragService.IngestFileAsync(file.FileName, stream);

        var documentDto = new DocumentDto(documentEntity.Id, documentEntity.FileName, documentEntity.UploadedAt);

        return Ok(documentDto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        var success = await _ragService.DeleteDocumentAsync(id);

        if (!success)
        {
            return NotFound(new { message = $"Document with ID {id} not found." });
        }

        return Ok(new { message = $"Document with ID {id} successfully deleted." });
    }
}