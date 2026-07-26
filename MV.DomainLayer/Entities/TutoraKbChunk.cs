using System;
using Pgvector;

namespace MV.DomainLayer.Entities;

public partial class TutoraKbChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string? Title { get; set; }

    public string Content { get; set; } = null!;

    public int ChunkIndex { get; set; }

    /// <summary>vector(768) — gemini-embedding-2, sinh bởi tutora-ai.</summary>
    public Vector? Embedding { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual TutoraKbDocument? Document { get; set; }
}
