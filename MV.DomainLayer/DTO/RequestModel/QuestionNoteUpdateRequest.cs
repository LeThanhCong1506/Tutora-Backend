using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Cập nhật phần học sinh sửa được của Note.
/// </summary>
public class QuestionNoteUpdateRequest
{
    [MaxLength(255)]
    public string? Title { get; set; }

    public string? PersonalNote { get; set; }

    public JsonElement? StepNotes { get; set; }

    // Phân loại — cho học sinh tự sửa khi bộ phân loại đoán sai, hoặc note cũ chưa có tag.

    [MaxLength(100)]
    public string? Subject { get; set; }

    [Range(1, 12, ErrorMessage = "Khối lớp phải từ 1 đến 12.")]
    public int? GradeLevel { get; set; }

    [MaxLength(120)]
    public string? Chapter { get; set; }
}
