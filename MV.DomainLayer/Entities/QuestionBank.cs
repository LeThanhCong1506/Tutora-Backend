using System;
using Pgvector;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Câu hỏi trong question bank (bảng `questions`) — dùng cho AI giải bài tập.
/// content = đề bài; solution = lời giải mẫu (thầy cô/Bộ GD). embedding sinh bởi
/// tutora-ai (POST /api/v1/embed) để semantic search khi học sinh gửi bài.
/// </summary>
public partial class QuestionBank
{
    public Guid Id { get; set; }

    public int SubjectId { get; set; }

    public int GradeLevelId { get; set; }

    /// <summary>[LEGACY] slug chương dạng text — giữ để tương thích data cũ. Ưu tiên dùng ChapterId.</summary>
    public string? Chapter { get; set; }

    /// <summary>[LEGACY] loại câu dạng text — giữ tương thích. Ưu tiên dùng QuestionTypeId.</summary>
    public string? ProblemType { get; set; }

    /// <summary>FK -> chapters. Chương gắn (môn, lớp). Null nếu chưa gán.</summary>
    public int? ChapterId { get; set; }

    /// <summary>FK -> question_types. Loại câu hỏi. Null nếu chưa gán.</summary>
    public int? QuestionTypeId { get; set; }

    /// <summary>4 cấp Bộ GD: NHAN_BIET | THONG_HIEU | VAN_DUNG | VAN_DUNG_CAO.</summary>
    public string? Difficulty { get; set; }

    public string Content { get; set; } = null!;

    /// <summary>Lời giải mẫu. Null nếu chưa có.</summary>
    public string? Solution { get; set; }

    /// <summary>Nguồn lời giải, vd "SGK Toán 9 - Kết nối tri thức".</summary>
    public string? SolutionSource { get; set; }

    /// <summary>URL ảnh (Cloudinary) của câu — bảng biến thiên/đồ thị crop từ PDF. jsonb array.</summary>
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>PDF nguồn nếu extract từ file. Null nếu nhập tay.</summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>Trang cụ thể trong PDF gốc, để staff đối chiếu.</summary>
    public int? SourcePage { get; set; }

    /// <summary>pending_review | published | rejected.</summary>
    public string ReviewStatus { get; set; } = "pending_review";

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>sha256(content) — tự tính bởi trigger DB khi content đổi.</summary>
    public string? ContentHash { get; set; }

    /// <summary>content_hash tại lần embed gần nhất. Khác content_hash = cần re-embed.</summary>
    public string? EmbeddedHash { get; set; }

    /// <summary>vector(768) — gemini-embedding-2. Sinh bởi tutora-ai /api/v1/embed. NULL = chưa embed.</summary>
    public Vector? Embedding { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Subject? Subject { get; set; }

    public virtual Gradelevel? Gradelevel { get; set; }

    public virtual SourceDocument? SourceDocument { get; set; }

    public virtual Chapter? ChapterNav { get; set; }

    public virtual QuestionType? QuestionType { get; set; }
}
