using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Cập nhật phần học sinh sửa được của Note: tiêu đề + ghi chú cá nhân.
/// KHÔNG cho sửa snapshot lời giải (giữ nguyên bản gốc AI trả).
/// </summary>
public class QuestionNoteUpdateRequest
{
    [MaxLength(255)]
    public string? Title { get; set; }

    public string? PersonalNote { get; set; }
}
