using System.Text;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Trả danh sách môn học & khối lớp (read-only) cho FE.
    /// Subject/GradeLevel khớp với GradeLevelId dùng trong pricing (SubjectGradePrices).
    /// </summary>
    public class LookupService : ILookupService
    {
        private readonly IAppDbContext _context;

        public LookupService(IAppDbContext context)
        {
            _context = context;
        }

        public async Task<List<SubjectResponse>> GetSubjectsAsync()
        {
            return await _context.Subjects
                .OrderBy(s => s.Subjectid)
                .Select(s => new SubjectResponse
                {
                    SubjectId = s.Subjectid,
                    SubjectName = s.Subjectname
                })
                .ToListAsync();
        }

        public async Task<List<GradeLevelResponse>> GetGradeLevelsAsync()
        {
            return await _context.Gradelevels
                .OrderBy(g => g.Levelorder)
                .Select(g => new GradeLevelResponse
                {
                    GradeLevelId = g.Gradelevelid,
                    GradeName = g.Gradename,
                    LevelOrder = g.Levelorder
                })
                .ToListAsync();
        }

        public async Task<List<ChapterResponse>> GetChaptersAsync(int? subjectId, int? gradeLevelId)
        {
            var query = _context.Chapters.Where(c => c.IsActive);
            if (subjectId.HasValue) query = query.Where(c => c.SubjectId == subjectId.Value);
            if (gradeLevelId.HasValue) query = query.Where(c => c.GradeLevelId == gradeLevelId.Value);

            return await query
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                .Select(c => new ChapterResponse
                {
                    Id = c.Id,
                    SubjectId = c.SubjectId,
                    GradeLevelId = c.GradeLevelId,
                    Slug = c.Slug,
                    Name = c.Name,
                    DisplayOrder = c.DisplayOrder,
                })
                .ToListAsync();
        }

        public async Task<List<QuestionTypeResponse>> GetQuestionTypesAsync()
        {
            return await _context.QuestionTypes
                .Where(t => t.IsActive)
                .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
                .Select(t => new QuestionTypeResponse
                {
                    Id = t.Id,
                    Slug = t.Slug,
                    Name = t.Name,
                    DisplayOrder = t.DisplayOrder,
                })
                .ToListAsync();
        }

        // Subject
        public async Task<SubjectResponse> CreateSubjectAsync(SubjectRequest req)
        {
            var entity = new Subject
            {
                Subjectname = req.SubjectName.Trim(),
                Description = req.Description?.Trim(),
            };
            _context.Subjects.Add(entity);
            await _context.SaveChangesAsync();
            return ToSubjectResponse(entity);
        }

        public async Task<SubjectResponse?> UpdateSubjectAsync(int id, SubjectRequest req)
        {
            var entity = await _context.Subjects.FirstOrDefaultAsync(s => s.Subjectid == id);
            if (entity == null) return null;
            entity.Subjectname = req.SubjectName.Trim();
            entity.Description = req.Description?.Trim();
            await _context.SaveChangesAsync();
            return ToSubjectResponse(entity);
        }

        public async Task<bool> DeleteSubjectAsync(int id)
        {
            var entity = await _context.Subjects.FirstOrDefaultAsync(s => s.Subjectid == id);
            if (entity == null) return false;
            if (await _context.QuestionBanks.AnyAsync(q => q.SubjectId == id))
                throw new LookupInUseException("Môn học này đang có câu hỏi. Vui lòng kiểm tra lại danh sách câu hỏi.");
            if (await _context.Chapters.AnyAsync(c => c.SubjectId == id))
                throw new LookupInUseException("Môn học này đang có chương. Vui lòng kiểm tra lại danh sách chương.");
            _context.Subjects.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        // GradeLevel
        public async Task<GradeLevelResponse> CreateGradeLevelAsync(GradeLevelRequest req)
        {
            var entity = new Gradelevel
            {
                Gradename = req.GradeName.Trim(),
                Levelorder = req.LevelOrder,
                Createdat = DateTime.UtcNow,
            };
            _context.Gradelevels.Add(entity);
            await _context.SaveChangesAsync();
            return ToGradeResponse(entity);
        }

        public async Task<GradeLevelResponse?> UpdateGradeLevelAsync(int id, GradeLevelRequest req)
        {
            var entity = await _context.Gradelevels.FirstOrDefaultAsync(g => g.Gradelevelid == id);
            if (entity == null) return null;
            entity.Gradename = req.GradeName.Trim();
            entity.Levelorder = req.LevelOrder;
            await _context.SaveChangesAsync();
            return ToGradeResponse(entity);
        }

        public async Task<bool> DeleteGradeLevelAsync(int id)
        {
            var entity = await _context.Gradelevels.FirstOrDefaultAsync(g => g.Gradelevelid == id);
            if (entity == null) return false;
            if (await _context.QuestionBanks.AnyAsync(q => q.GradeLevelId == id))
                throw new LookupInUseException("Khối lớp này đang có câu hỏi. Vui lòng kiểm tra lại danh sách câu hỏi.");
            if (await _context.Chapters.AnyAsync(c => c.GradeLevelId == id))
                throw new LookupInUseException("Khối lớp này đang có chương. Vui lòng kiểm tra lại danh sách chương.");
            _context.Gradelevels.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        // Chapter
        public async Task<ChapterResponse> CreateChapterAsync(ChapterRequest req)
        {
            var entity = new Chapter
            {
                SubjectId = req.SubjectId,
                GradeLevelId = req.GradeLevelId,
                Name = req.Name.Trim(),
                Slug = string.IsNullOrWhiteSpace(req.Slug) ? Slugify(req.Name) : req.Slug!.Trim(),
                DisplayOrder = req.DisplayOrder,
                IsActive = req.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _context.Chapters.Add(entity);
            await _context.SaveChangesAsync();
            return ToChapterResponse(entity);
        }

        public async Task<ChapterResponse?> UpdateChapterAsync(int id, ChapterRequest req)
        {
            var entity = await _context.Chapters.FirstOrDefaultAsync(c => c.Id == id);
            if (entity == null) return null;
            entity.SubjectId = req.SubjectId;
            entity.GradeLevelId = req.GradeLevelId;
            entity.Name = req.Name.Trim();
            entity.Slug = string.IsNullOrWhiteSpace(req.Slug) ? Slugify(req.Name) : req.Slug!.Trim();
            entity.DisplayOrder = req.DisplayOrder;
            entity.IsActive = req.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ToChapterResponse(entity);
        }

        public async Task<bool> DeleteChapterAsync(int id)
        {
            var entity = await _context.Chapters.FirstOrDefaultAsync(c => c.Id == id);
            if (entity == null) return false;
            if (await _context.QuestionBanks.AnyAsync(q => q.ChapterId == id))
                throw new LookupInUseException("Chương này đang có câu hỏi. Vui lòng kiểm tra lại danh sách câu hỏi.");
            _context.Chapters.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        // QuestionType
        public async Task<QuestionTypeResponse> CreateQuestionTypeAsync(QuestionTypeRequest req)
        {
            var entity = new QuestionType
            {
                Name = req.Name.Trim(),
                Slug = string.IsNullOrWhiteSpace(req.Slug) ? Slugify(req.Name) : req.Slug!.Trim(),
                DisplayOrder = req.DisplayOrder,
                IsActive = req.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _context.QuestionTypes.Add(entity);
            await _context.SaveChangesAsync();
            return ToQuestionTypeResponse(entity);
        }

        public async Task<QuestionTypeResponse?> UpdateQuestionTypeAsync(int id, QuestionTypeRequest req)
        {
            var entity = await _context.QuestionTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (entity == null) return null;
            entity.Name = req.Name.Trim();
            entity.Slug = string.IsNullOrWhiteSpace(req.Slug) ? Slugify(req.Name) : req.Slug!.Trim();
            entity.DisplayOrder = req.DisplayOrder;
            entity.IsActive = req.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ToQuestionTypeResponse(entity);
        }

        public async Task<bool> DeleteQuestionTypeAsync(int id)
        {
            var entity = await _context.QuestionTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (entity == null) return false;
            if (await _context.QuestionBanks.AnyAsync(q => q.QuestionTypeId == id))
                throw new LookupInUseException("Loại câu hỏi này đang có câu hỏi. Vui lòng kiểm tra lại danh sách câu hỏi.");
            _context.QuestionTypes.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        // Helpers
        private static SubjectResponse ToSubjectResponse(Subject s) => new()
        {
            SubjectId = s.Subjectid,
            SubjectName = s.Subjectname,
        };

        private static GradeLevelResponse ToGradeResponse(Gradelevel g) => new()
        {
            GradeLevelId = g.Gradelevelid,
            GradeName = g.Gradename,
            LevelOrder = g.Levelorder,
        };

        private static ChapterResponse ToChapterResponse(Chapter c) => new()
        {
            Id = c.Id,
            SubjectId = c.SubjectId,
            GradeLevelId = c.GradeLevelId,
            Slug = c.Slug,
            Name = c.Name,
            DisplayOrder = c.DisplayOrder,
        };

        private static QuestionTypeResponse ToQuestionTypeResponse(QuestionType t) => new()
        {
            Id = t.Id,
            Slug = t.Slug,
            Name = t.Name,
            DisplayOrder = t.DisplayOrder,
        };

        /// <summary>Sinh slug ASCII từ tên tiếng Việt (bỏ dấu, thay khoảng trắng bằng _).</summary>
        private static string Slugify(string input)
        {
            var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in normalized)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('_');
                // đ -> d
            }
            var slug = sb.ToString().Replace("đ", "d").Normalize(NormalizationForm.FormC);
            while (slug.Contains("__")) slug = slug.Replace("__", "_");
            return slug.Trim('_');
        }
    }
}
