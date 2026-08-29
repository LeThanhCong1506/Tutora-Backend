using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Toàn văn đã trích xuất của 1 tài liệu học tập (`learning_material_contents`).
///
/// Trích 1 lần lúc upload để lúc gia sư bấm "Tạo câu hỏi" giữa buổi dạy không phải
/// tải file về parse lại. Một dòng / một tài liệu — khoá chính chính là MaterialId.
///
/// KHÔNG chunk, KHÔNG vector: gia sư chọn thẳng tài liệu nên không có truy vấn nào
/// để retrieve; cả file được nhét vào prompt.
/// </summary>
public partial class LearningMaterialContent
{
    /// <summary>FK -> learning_materials.material_id, đồng thời là khoá chính.</summary>
    public int MaterialId { get; set; }

    /// <summary>Toàn văn, có chèn mốc "[trang N]" để AI trích dẫn được số trang.</summary>
    public string FullText { get; set; } = null!;

    public int? PageCount { get; set; }

    /// <summary>processing | ready | failed.</summary>
    public string Status { get; set; } = "processing";

    /// <summary>Lý do trích xuất hỏng — hiện cho gia sư biết vì sao file không dùng được.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime ExtractedAt { get; set; }

    public virtual Learningmaterial? Material { get; set; }
}
