using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// 1 tài liệu Knowledge Base Tutora
public partial class TutoraKbDocument
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = null!;

    /// <summary>pdf | docx | xlsx | manual.</summary>
    public string SourceType { get; set; } = null!;

    public int ChunkCount { get; set; }

    /// <summary>processing | ready | failed.</summary>
    public string Status { get; set; } = "ready";

    public string? UploadedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<TutoraKbChunk> Chunks { get; set; } = new List<TutoraKbChunk>();
}
