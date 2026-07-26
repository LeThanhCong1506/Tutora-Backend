namespace MV.DomainLayer.DTO.ResponseModel.KnowledgeBase;

public class KbDocumentResponse
{
    public string Id { get; set; } = null!;
    public string FileName { get; set; } = null!;
    // pdf | docx | xlsx | manual
    public string SourceType { get; set; } = null!;   
    public int ChunkCount { get; set; }
    // processing | ready | failed
    public string Status { get; set; } = null!;        
    public DateTime? CreatedAt { get; set; }
}

/// <summary>Kết quả nạp 1 tài liệu KB.</summary>
public class KbUploadResponse
{
    public string DocumentId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public int ChunkCount { get; set; }
}
