using CsvHelper;
using LocalRAGChat.Server.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace LocalRAGChat.Server.Data;

public static class CsvSeeding
{
    public class DocumentCsv
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public DateTime UploadedAt { get; set; }
    }
    public class DocumentChunkCsv
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Content { get; set; }
        public string EmbeddingJson { get; set; } = string.Empty;
        
    }
}