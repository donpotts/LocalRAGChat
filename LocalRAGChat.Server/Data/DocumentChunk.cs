using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace LocalRAGChat.Server.Data;

public class DocumentChunk
{
    [Key]
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; }

    public string Content { get; set; }

    [NotMapped]
    public float[] Embedding { get; set; } = [];

    public string EmbeddingJson { get; set; } = string.Empty;
}