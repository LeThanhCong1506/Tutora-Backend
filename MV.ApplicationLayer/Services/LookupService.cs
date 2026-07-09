using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Question;

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
    }
}
