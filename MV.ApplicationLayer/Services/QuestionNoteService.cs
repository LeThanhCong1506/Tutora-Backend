using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public class QuestionNoteService(IQuestionNoteRepository repo) : IQuestionNoteService
{
    public async Task<List<QuestionNoteResponse>> GetMyNotesAsync(string userId, string? subject = null, int? gradeLevel = null)
    {
        var notes = await repo.GetByUserAsync(userId, subject, gradeLevel);
        // List không kèm snapshot steps để nhẹ payload — chi tiết mới trả đủ.
        return notes.Select(n => ToResponse(n, includeSteps: false)).ToList();
    }

    public async Task<QuestionNoteResponse> GetNoteAsync(string userId, Guid noteId)
    {
        var note = await GetOwnedNoteAsync(userId, noteId);
        return ToResponse(note, includeSteps: true);
    }

    public async Task<QuestionNoteResponse> CreateNoteAsync(string userId, QuestionNoteCreateRequest dto)
    {
        var existing = await repo.FindDuplicateAsync(userId, dto.SourceSessionId, dto.Title);
        if (existing is not null)
            return ToResponse(existing, includeSteps: true);

        var now = TimeZoneHelper.UtcNow;
        var note = new QuestionNote
        {
            NoteId = Guid.NewGuid(),
            UserId = userId,
            SourceSessionId = dto.SourceSessionId,
            Title = dto.Title,
            ProblemText = dto.ProblemText,
            ProblemImageUrl = dto.ProblemImageUrl,
            // JsonElement -> raw JSON text cho cột jsonb. Mặc định mảng rỗng.
            SolutionSteps = dto.SolutionSteps is { ValueKind: JsonValueKind.Array } s ? s.GetRawText() : "[]",
            AnswerSummary = dto.AnswerSummary,
            PersonalNote = dto.PersonalNote,
            Subject = dto.Subject,
            GradeLevel = dto.GradeLevel,
            Chapter = dto.Chapter,
            CreatedAt = now,
            UpdatedAt = now,
        };

        repo.Add(note);
        try
        {
            await repo.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: 2 request đồng thời cùng lách qua check ở trên -> unique index chặn
            // request sau. Idempotent: trả note đã tạo thay vì ném 500.
            var dup = await repo.FindDuplicateAsync(userId, dto.SourceSessionId, dto.Title);
            if (dup is not null) return ToResponse(dup, includeSteps: true);
            throw;
        }
        return ToResponse(note, includeSteps: true);
    }

    public async Task<QuestionNoteResponse> UpdateNoteAsync(string userId, Guid noteId, QuestionNoteUpdateRequest dto)
    {
        var note = await GetOwnedNoteAsync(userId, noteId);

        if (dto.Title is not null) note.Title = dto.Title;
        if (dto.PersonalNote is not null) note.PersonalNote = dto.PersonalNote;
        if (dto.Subject is not null) note.Subject = dto.Subject;
        if (dto.GradeLevel is not null) note.GradeLevel = dto.GradeLevel;
        if (dto.Chapter is not null) note.Chapter = dto.Chapter;

        // Chỉ nhận object; mảng/chuỗi lọt vào sẽ làm FE vỡ khi đọc theo khoá bước.
        if (dto.StepNotes is { ValueKind: JsonValueKind.Object } sn)
            note.StepNotes = sn.GetRawText();

        note.UpdatedAt = TimeZoneHelper.UtcNow;

        repo.Update(note);
        await repo.SaveChangesAsync();
        return ToResponse(note, includeSteps: true);
    }

    public async Task DeleteNoteAsync(string userId, Guid noteId)
    {
        var note = await GetOwnedNoteAsync(userId, noteId);
        repo.Remove(note);
        await repo.SaveChangesAsync();
    }

    private async Task<QuestionNote> GetOwnedNoteAsync(string userId, Guid noteId)
    {
        var note = await repo.FindByIdAsync(noteId)
            ?? throw new QuestionNoteNotFoundException(noteId);
        if (note.UserId != userId)
            throw new QuestionNoteForbiddenException();
        return note;
    }

    private static QuestionNoteResponse ToResponse(QuestionNote n, bool includeSteps) => new()
    {
        NoteId = n.NoteId.ToString(),
        SourceSessionId = n.SourceSessionId?.ToString(),
        Title = n.Title,
        ProblemText = n.ProblemText,
        ProblemImageUrl = n.ProblemImageUrl,
        SolutionSteps = includeSteps && !string.IsNullOrEmpty(n.SolutionSteps)
            ? JsonSerializer.Deserialize<JsonElement>(n.SolutionSteps)
            : null,
        AnswerSummary = n.AnswerSummary,
        PersonalNote = n.PersonalNote,
        // Ghi chú bước chỉ có nghĩa khi có steps để gắn vào -> theo cùng includeSteps.
        StepNotes = includeSteps && !string.IsNullOrEmpty(n.StepNotes)
            ? JsonSerializer.Deserialize<JsonElement>(n.StepNotes)
            : null,
        Subject = n.Subject,
        GradeLevel = n.GradeLevel,
        Chapter = n.Chapter,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt,
    };
}
