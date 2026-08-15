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
            // display_order do admin đặt; cùng giá trị thì xếp theo tên cho ổn định.
            return await _context.Subjects
                .Where(s => s.IsActive)
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.Subjectname)
                .Select(s => new SubjectResponse
                {
                    SubjectId = s.Subjectid,
                    SubjectName = s.Subjectname,
                    Description = s.Description,
                    IsActive = s.IsActive,
                    Slug = s.Slug,
                    IconUrl = s.IconUrl,
                    IsHomeworkEnabled = s.IsHomeworkEnabled,
                    DisplayOrder = s.DisplayOrder,
                    MinGradeLevelId = s.MinGradeLevelId,
                    MaxGradeLevelId = s.MaxGradeLevelId
                })
                .ToListAsync();
        }

        public async Task<List<GradeLevelResponse>> GetGradeLevelsAsync()
        {
            return await _context.Gradelevels
                .Where(g => g.IsActive)
                .OrderBy(g => g.Levelorder)
                .Select(g => new GradeLevelResponse
                {
                    GradeLevelId = g.Gradelevelid,
                    GradeName = g.Gradename,
                    LevelOrder = g.Levelorder,
                    IsActive = g.IsActive
                })
                .ToListAsync();
        }

        // Admin: trả TẤT CẢ (gồm cả mục đã ngừng dùng) để quản lý & khôi phục.
        public async Task<List<SubjectResponse>> GetAllSubjectsAsync()
        {
            return await _context.Subjects
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.Subjectname)
                .Select(s => new SubjectResponse
                {
                    SubjectId = s.Subjectid,
                    SubjectName = s.Subjectname,
                    Description = s.Description,
                    IsActive = s.IsActive,
                    Slug = s.Slug,
                    IconUrl = s.IconUrl,
                    IsHomeworkEnabled = s.IsHomeworkEnabled,
                    DisplayOrder = s.DisplayOrder,
                    MinGradeLevelId = s.MinGradeLevelId,
                    MaxGradeLevelId = s.MaxGradeLevelId
                })
                .ToListAsync();
        }

        public async Task<List<GradeLevelResponse>> GetAllGradeLevelsAsync()
        {
            return await _context.Gradelevels
                .OrderBy(g => g.Levelorder)
                .Select(g => new GradeLevelResponse
                {
                    GradeLevelId = g.Gradelevelid,
                    GradeName = g.Gradename,
                    LevelOrder = g.Levelorder,
                    IsActive = g.IsActive
                })
                .ToListAsync();
        }

        // Admin: KHÔNG lọc IsActive.
        // QuestionCount cho biết trước xoá có bị chặn 409 hay không.
        public async Task<List<AdminChapterResponse>> GetAllChaptersAsync(int? subjectId, int? gradeLevelId)
        {
            var query = _context.Chapters.AsQueryable();
            if (subjectId.HasValue) query = query.Where(c => c.SubjectId == subjectId.Value);
            if (gradeLevelId.HasValue) query = query.Where(c => c.GradeLevelId == gradeLevelId.Value);

            return await query
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                .Select(c => new AdminChapterResponse
                {
                    Id = c.Id,
                    SubjectId = c.SubjectId,
                    GradeLevelId = c.GradeLevelId,
                    Slug = c.Slug,
                    Name = c.Name,
                    DisplayOrder = c.DisplayOrder,
                    SubjectName = c.Subject != null ? c.Subject.Subjectname : null,
                    GradeName = c.Gradelevel != null ? c.Gradelevel.Gradename : null,
                    IsActive = c.IsActive,
                    QuestionCount = c.Questions.Count,
                })
                .ToListAsync();
        }

        public async Task<List<AdminQuestionTypeResponse>> GetAllQuestionTypesAsync()
        {
            return await _context.QuestionTypes
                .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
                .Select(t => new AdminQuestionTypeResponse
                {
                    Id = t.Id,
                    Slug = t.Slug,
                    Name = t.Name,
                    DisplayOrder = t.DisplayOrder,
                    IsActive = t.IsActive,
                    QuestionCount = _context.QuestionBanks.Count(q => q.QuestionTypeId == t.Id),
                })
                .ToListAsync();
        }

        public async Task<List<DayOfWeekResponse>> GetDaysOfWeekAsync()
        {
            return await _context.DaysOfWeek
                .OrderBy(d => d.DayOrder)
                .Select(d => new DayOfWeekResponse
                {
                    DayOfWeekId = d.DayofweekId,
                    DayName = d.DayName,
                    DayOrder = d.DayOrder
                })
                .ToListAsync();
        }

        public async Task<List<ChapterResponse>> GetChaptersAsync(int? subjectId, int? gradeLevelId)
        {
            // Ẩn chương thuộc môn/khối đã bị soft-delete (Subject/Gradelevel.IsActive), không chỉ Chapter.IsActive.
            var query = _context.Chapters.Where(c => c.IsActive
                && c.Subject != null && c.Subject.IsActive
                && c.Gradelevel != null && c.Gradelevel.IsActive);
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

        // Reorder
        public Task<bool> ReorderSubjectsAsync(ReorderRequest req)
            => ReorderAsync(req, ids => _context.Subjects.Where(s => ids.Contains(s.Subjectid)),
                s => s.Subjectid, (s, order) => s.DisplayOrder = order);

        public Task<bool> ReorderGradeLevelsAsync(ReorderRequest req)
            => ReorderAsync(req, ids => _context.Gradelevels.Where(g => ids.Contains(g.Gradelevelid)),
                g => g.Gradelevelid, (g, order) => g.Levelorder = order);

        public Task<bool> ReorderChaptersAsync(ReorderRequest req)
            => ReorderAsync(req, ids => _context.Chapters.Where(c => ids.Contains(c.Id)),
                c => c.Id, (c, order) => { c.DisplayOrder = order; c.UpdatedAt = DateTime.UtcNow; });

        public Task<bool> ReorderQuestionTypesAsync(ReorderRequest req)
            => ReorderAsync(req, ids => _context.QuestionTypes.Where(t => ids.Contains(t.Id)),
                t => t.Id, (t, order) => { t.DisplayOrder = order; t.UpdatedAt = DateTime.UtcNow; });

        /// <summary>
        /// Nạp đúng các bản ghi theo id, gán thứ tự mới rồi lưu trong 1 transaction.
        /// </summary>
        private async Task<bool> ReorderAsync<TEntity>(
            ReorderRequest req,
            Func<List<int>, IQueryable<TEntity>> query,
            Func<TEntity, int> idOf,
            Action<TEntity, int> setOrder) where TEntity : class
        {
            var ids = req.Items.Select(i => i.Id).Distinct().ToList();
            var entities = await query(ids).ToListAsync();
            if (entities.Count != ids.Count) return false;

            var orderById = req.Items.ToDictionary(i => i.Id, i => i.DisplayOrder);

            await using var tx = await _context.Database.BeginTransactionAsync();
            foreach (var entity in entities)
                setOrder(entity, orderById[idOf(entity)]);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        // Subject
        public async Task<SubjectResponse> CreateSubjectAsync(SubjectRequest req)
        {
            await ValidateGradeRangeAsync(req.MinGradeLevelId, req.MaxGradeLevelId);

            var entity = new Subject
            {
                Subjectname = req.SubjectName.Trim(),
                Description = req.Description?.Trim(),
                IsActive = req.IsActive,
                Slug = NormalizeSlug(req.Slug, req.SubjectName),
                IconUrl = req.IconUrl?.Trim(),
                IsHomeworkEnabled = req.IsHomeworkEnabled,
                DisplayOrder = req.DisplayOrder,
                MinGradeLevelId = req.MinGradeLevelId,
                MaxGradeLevelId = req.MaxGradeLevelId,
            };
            _context.Subjects.Add(entity);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueSubjectSlugConflict(ex))
            {
                throw new ArgumentException($"Slug \"{entity.Slug}\" đã được dùng cho môn học khác. Vui lòng đổi tên hoặc nhập slug khác.");
            }
            catch (DbUpdateException ex) when (IsUniqueSubjectNameConflict(ex))
            {
                throw new ArgumentException($"Tên môn học \"{entity.Subjectname}\" đã tồn tại.");
            }
            return ToSubjectResponse(entity);
        }

        public async Task<SubjectResponse?> UpdateSubjectAsync(int id, SubjectRequest req)
        {
            await ValidateGradeRangeAsync(req.MinGradeLevelId, req.MaxGradeLevelId);

            var entity = await _context.Subjects.FirstOrDefaultAsync(s => s.Subjectid == id);
            if (entity == null) return null;
            entity.Subjectname = req.SubjectName.Trim();
            entity.Description = req.Description?.Trim();
            entity.IsActive = req.IsActive;
            entity.Slug = NormalizeSlug(req.Slug, req.SubjectName);
            entity.IconUrl = req.IconUrl?.Trim();
            entity.IsHomeworkEnabled = req.IsHomeworkEnabled;
            entity.DisplayOrder = req.DisplayOrder;
            entity.MinGradeLevelId = req.MinGradeLevelId;
            entity.MaxGradeLevelId = req.MaxGradeLevelId;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueSubjectSlugConflict(ex))
            {
                throw new ArgumentException($"Slug \"{entity.Slug}\" đã được dùng cho môn học khác. Vui lòng đổi tên hoặc nhập slug khác.");
            }
            catch (DbUpdateException ex) when (IsUniqueSubjectNameConflict(ex))
            {
                throw new ArgumentException($"Tên môn học \"{entity.Subjectname}\" đã tồn tại.");
            }
            return ToSubjectResponse(entity);
        }

        // Khớp convention IsUniquePhoneConflict trong SimpleAuthService: soi tên constraint
        // trong message lỗi Postgres thay vì bắt theo SqlState (DbUpdateException không tự lộ SqlState).
        private static bool IsUniqueSubjectSlugConflict(DbUpdateException ex) =>
            $"{ex.Message} {ex.InnerException?.Message}".Contains("ux_subjects_slug", StringComparison.OrdinalIgnoreCase);

        private static bool IsUniqueSubjectNameConflict(DbUpdateException ex) =>
            $"{ex.Message} {ex.InnerException?.Message}".Contains("subjects_subjectname_key", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// So theo Levelorder (không so trực tiếp id — id không đảm bảo tăng dần theo khối lớp).
        /// </summary>
        private async Task ValidateGradeRangeAsync(int? minGradeLevelId, int? maxGradeLevelId)
        {
            if (minGradeLevelId == null || maxGradeLevelId == null) return;

            var orders = await _context.Gradelevels
                .Where(g => g.Gradelevelid == minGradeLevelId || g.Gradelevelid == maxGradeLevelId)
                .ToDictionaryAsync(g => g.Gradelevelid, g => g.Levelorder);

            if (!orders.TryGetValue(minGradeLevelId.Value, out var minOrder))
                throw new ArgumentException($"Khối lớp tối thiểu (id={minGradeLevelId}) không tồn tại.");
            if (!orders.TryGetValue(maxGradeLevelId.Value, out var maxOrder))
                throw new ArgumentException($"Khối lớp tối đa (id={maxGradeLevelId}) không tồn tại.");
            if (maxOrder < minOrder)
                throw new ArgumentException("Khối lớp tối đa phải lớn hơn hoặc bằng khối lớp tối thiểu.");
        }

        // Soft-delete: chỉ đặt IsActive=false, GIỮ NGUYÊN dữ liệu để không phá tham chiếu
        // (câu hỏi, chương, bảng giá, hồ sơ...). Khôi phục bằng Update IsActive=true.
        // Không cascade sang Tutorsubjectgradeprice: booking/lớp đang dạy dựa trên môn này
        // phải tiếp tục hoạt động bình thường; chỉ báo số lượng bị ảnh hưởng để Admin/Staff biết.
        public async Task<LookupDeleteResult> DeleteSubjectAsync(int id)
        {
            var entity = await _context.Subjects.FirstOrDefaultAsync(s => s.Subjectid == id);
            if (entity == null) return new LookupDeleteResult(false, 0);

            var affectedCount = await _context.Tutorsubjectgradeprices
                .CountAsync(p => p.Subjectid == id && p.Isactive);

            entity.IsActive = false;
            await _context.SaveChangesAsync();
            return new LookupDeleteResult(true, affectedCount);
        }

        /// <summary>
        /// Xóa vĩnh viễn một môn học. Yêu cầu môn đang IsActive=false (phải ngừng hoạt động
        /// trước) và chưa từng được tham chiếu bởi Chapter/QuestionBank/Tutorsubjectgradeprice/
        /// Studentgrade (Booking tham chiếu Subject gián tiếp qua Tutorsubjectgradeprice nên
        /// đã được bao phủ). Ném LookupInUseException nếu còn tham chiếu -> controller trả 409.
        /// </summary>
        public async Task<bool> HardDeleteSubjectAsync(int id)
        {
            var entity = await _context.Subjects.FirstOrDefaultAsync(s => s.Subjectid == id);
            if (entity == null) return false;

            if (entity.IsActive)
                throw new InvalidOperationException("Vui lòng ngừng hoạt động môn học này trước khi xóa vĩnh viễn.");

            var inUse = await _context.Chapters.AnyAsync(c => c.SubjectId == id)
                || await _context.QuestionBanks.AnyAsync(q => q.SubjectId == id)
                || await _context.Tutorsubjectgradeprices.AnyAsync(p => p.Subjectid == id)
                || await _context.Studentgrades.AnyAsync(sg => sg.Subjectid == id);
            if (inUse)
                throw new LookupInUseException(
                    "Không thể xóa vĩnh viễn môn học này vì đang được tham chiếu bởi chương, câu hỏi, bảng giá gia sư hoặc hồ sơ học sinh. Bạn có thể tiếp tục giữ ở trạng thái ngừng hoạt động.");

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
                IsActive = req.IsActive,
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
            entity.IsActive = req.IsActive;
            await _context.SaveChangesAsync();
            return ToGradeResponse(entity);
        }

        // Soft-delete: chỉ đặt IsActive=false, GIỮ NGUYÊN dữ liệu (câu hỏi, chương,
        // bảng giá, hồ sơ học sinh...). Khôi phục bằng Update IsActive=true.
        // Không cascade sang Tutorsubjectgradeprice: booking/lớp đang dạy dựa trên khối lớp này
        // phải tiếp tục hoạt động bình thường; chỉ báo số lượng bị ảnh hưởng để Admin/Staff biết.
        public async Task<LookupDeleteResult> DeleteGradeLevelAsync(int id)
        {
            var entity = await _context.Gradelevels.FirstOrDefaultAsync(g => g.Gradelevelid == id);
            if (entity == null) return new LookupDeleteResult(false, 0);

            var affectedCount = await _context.Tutorsubjectgradeprices
                .CountAsync(p => p.Gradelevelid == id && p.Isactive);

            entity.IsActive = false;
            await _context.SaveChangesAsync();
            return new LookupDeleteResult(true, affectedCount);
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
        /// <summary>
        /// Chuẩn hoá slug: bỏ dấu tiếng Việt, chữ thường, nối bằng "-" (vd: toan-hoc).
        /// Bỏ trống thì tự sinh từ tên môn.
        /// </summary>
        private static string? NormalizeSlug(string? slug, string subjectName)
        {
            var source = string.IsNullOrWhiteSpace(slug) ? subjectName : slug;
            if (string.IsNullOrWhiteSpace(source)) return null;

            // đ/Đ không phải nguyên âm có dấu -> FormD không tách được, phải thay tay.
            var normalized = source.Replace('đ', 'd').Replace('Đ', 'D')
                .Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            }

            // Gộp dấu "-" liên tiếp và cắt hai đầu.
            var parts = builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries);
            var result = string.Join('-', parts);
            return result.Length == 0 ? null : result;
        }

        private static SubjectResponse ToSubjectResponse(Subject s) => new()
        {
            SubjectId = s.Subjectid,
            SubjectName = s.Subjectname,
            Description = s.Description,
            IsActive = s.IsActive,
            Slug = s.Slug,
            IconUrl = s.IconUrl,
            IsHomeworkEnabled = s.IsHomeworkEnabled,
            DisplayOrder = s.DisplayOrder,
            MinGradeLevelId = s.MinGradeLevelId,
            MaxGradeLevelId = s.MaxGradeLevelId,
        };

        private static GradeLevelResponse ToGradeResponse(Gradelevel g) => new()
        {
            GradeLevelId = g.Gradelevelid,
            GradeName = g.Gradename,
            LevelOrder = g.Levelorder,
            IsActive = g.IsActive,
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
