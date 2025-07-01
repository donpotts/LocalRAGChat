using System.ComponentModel.DataAnnotations;

namespace LocalRAGChat.Server.Data;

public class Document
{
    [Key]
    public int Id { get; set; }
    public string FileName { get; set; }
    public DateTime UploadedAt { get; set; }
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}