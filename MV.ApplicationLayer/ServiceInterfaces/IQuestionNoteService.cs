using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Quản lý Note do học sinh tạo từ Interactive Solution Canvas (question_notes).
/// Tách hoàn toàn với lịch sử chat (IAiChatService).
/// </summary>
public interface IQuestionNoteService
{
    /// <summary>Danh sách note của học sinh, lọc theo môn/lớp nếu có. KHÔNG kèm snapshot lời giải (nhẹ).</summary>
    Task<List<QuestionNoteResponse>> GetMyNotesAsync(string userId, string? subject = null, int? gradeLevel = null);

    /// <summary>Chi tiết 1 note (kèm snapshot lời giải để render canvas) — chỉ chủ note.</summary>
    Task<QuestionNoteResponse> GetNoteAsync(string userId, Guid noteId);

    /// <summary>Lưu một lời giải canvas thành note mới.</summary>
    Task<QuestionNoteResponse> CreateNoteAsync(string userId, QuestionNoteCreateRequest dto);

    /// <summary>Sửa tiêu đề / ghi chú cá nhân của note — chỉ chủ note.</summary>
    Task<QuestionNoteResponse> UpdateNoteAsync(string userId, Guid noteId, QuestionNoteUpdateRequest dto);

    /// <summary>Xoá note — chỉ chủ note.</summary>
    Task DeleteNoteAsync(string userId, Guid noteId);
}
