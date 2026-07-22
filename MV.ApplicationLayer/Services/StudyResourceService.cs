using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Trang Tài nguyên công khai. Chỉ đọc câu hỏi review_status = "published".
    /// </summary>
    public class StudyResourceService : IStudyResourceService
    {
        private const string Published = "published";

        private readonly IAppDbContext _context;

        public StudyResourceService(IAppDbContext context)
        {
            _context = context;
        }

        public async Task<PagedList<PublicQuestionResponse>?> GetQuestionsAsync(
            string subjectSlug, string? chapterSlug, int pageNumber, int pageSize,
            string? userId, CancellationToken ct = default)
        {
            // Môn phải tồn tại + đang active. Slug so khớp không phân biệt hoa thường.
            var subject = await _context.Subjects
                .FirstOrDefaultAsync(s => s.IsActive && s.Slug != null
                    && s.Slug.ToLower() == subjectSlug.ToLower(), ct);
            if (subject == null) return null;

            int? chapterId = null;
            if (!string.IsNullOrWhiteSpace(chapterSlug))
            {
                var chapter = await _context.Chapters.FirstOrDefaultAsync(
                    c => c.IsActive && c.SubjectId == subject.Subjectid
                        && c.Slug.ToLower() == chapterSlug.ToLower(), ct);
                if (chapter == null) return null;
                chapterId = chapter.Id;
            }

            var query = _context.QuestionBanks
                .Where(q => q.ReviewStatus == Published && q.SubjectId == subject.Subjectid);
            if (chapterId.HasValue) query = query.Where(q => q.ChapterId == chapterId.Value);

            var totalCount = await query.CountAsync(ct);

            // Đếm like/dislike theo từng câu bằng subquery aggregate trên question_votes.
            // Sắp: nhiều like trước, rồi mới nhất trước.
            var rows = await query
                .Select(q => new
                {
                    Question = q,
                    SubjectName = q.Subject!.Subjectname,
                    ChapterName = q.ChapterNav != null ? q.ChapterNav.Name : null,
                    QuestionTypeName = q.QuestionType != null ? q.QuestionType.Name : null,
                    LikeCount = _context.QuestionVotes.Count(v => v.QuestionId == q.Id && v.Vote == 1),
                    DislikeCount = _context.QuestionVotes.Count(v => v.QuestionId == q.Id && v.Vote == -1),
                    MyVote = userId == null
                        ? (short?)null
                        : _context.QuestionVotes
                            .Where(v => v.QuestionId == q.Id && v.UserId == userId)
                            .Select(v => (short?)v.Vote)
                            .FirstOrDefault(),
                })
                .OrderByDescending(x => x.LikeCount)
                .ThenByDescending(x => x.Question.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var items = rows.Select(x => new PublicQuestionResponse
            {
                Id = x.Question.Id,
                Content = x.Question.Content,
                Solution = x.Question.Solution,
                SolutionSource = x.Question.SolutionSource,
                ImageUrls = x.Question.ImageUrls,
                Difficulty = x.Question.Difficulty,
                SubjectName = x.SubjectName,
                ChapterName = x.ChapterName,
                QuestionTypeName = x.QuestionTypeName,
                LikeCount = x.LikeCount,
                DislikeCount = x.DislikeCount,
                HelpfulPercent = HelpfulPercent(x.LikeCount, x.DislikeCount),
                MyVote = x.MyVote,
                CreatedAt = x.Question.CreatedAt,
            }).ToList();

            return new PagedList<PublicQuestionResponse>(items, totalCount, pageNumber, pageSize);
        }

        public async Task<PublicQuestionResponse?> GetByIdAsync(
            Guid questionId, string? userId, CancellationToken ct = default)
        {
            var row = await _context.QuestionBanks
                .Where(q => q.Id == questionId && q.ReviewStatus == Published)
                .Select(q => new
                {
                    Question = q,
                    SubjectName = q.Subject!.Subjectname,
                    ChapterName = q.ChapterNav != null ? q.ChapterNav.Name : null,
                    QuestionTypeName = q.QuestionType != null ? q.QuestionType.Name : null,
                    LikeCount = _context.QuestionVotes.Count(v => v.QuestionId == q.Id && v.Vote == 1),
                    DislikeCount = _context.QuestionVotes.Count(v => v.QuestionId == q.Id && v.Vote == -1),
                    MyVote = userId == null
                        ? (short?)null
                        : _context.QuestionVotes
                            .Where(v => v.QuestionId == q.Id && v.UserId == userId)
                            .Select(v => (short?)v.Vote)
                            .FirstOrDefault(),
                })
                .FirstOrDefaultAsync(ct);

            if (row == null) return null;

            return new PublicQuestionResponse
            {
                Id = row.Question.Id,
                Content = row.Question.Content,
                Solution = row.Question.Solution,
                SolutionSource = row.Question.SolutionSource,
                ImageUrls = row.Question.ImageUrls,
                Difficulty = row.Question.Difficulty,
                SubjectName = row.SubjectName,
                ChapterName = row.ChapterName,
                QuestionTypeName = row.QuestionTypeName,
                LikeCount = row.LikeCount,
                DislikeCount = row.DislikeCount,
                HelpfulPercent = HelpfulPercent(row.LikeCount, row.DislikeCount),
                MyVote = row.MyVote,
                CreatedAt = row.Question.CreatedAt,
            };
        }

        public async Task<QuestionVoteResponse?> VoteAsync(
            Guid questionId, string userId, int vote, CancellationToken ct = default)
        {
            // Chỉ cho vote câu đã published (khớp thứ FE hiển thị).
            var exists = await _context.QuestionBanks
                .AnyAsync(q => q.Id == questionId && q.ReviewStatus == Published, ct);
            if (!exists) return null;

            var existing = await _context.QuestionVotes
                .FirstOrDefaultAsync(v => v.QuestionId == questionId && v.UserId == userId, ct);

            if (vote == 0)
            {
                // Bỏ vote.
                if (existing != null) _context.QuestionVotes.Remove(existing);
            }
            else
            {
                var value = (short)(vote > 0 ? 1 : -1);
                if (existing == null)
                {
                    _context.QuestionVotes.Add(new QuestionVote
                    {
                        QuestionId = questionId,
                        UserId = userId,
                        Vote = value,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
                else if (existing.Vote != value)
                {
                    existing.Vote = value;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(ct);

            var likeCount = await _context.QuestionVotes.CountAsync(v => v.QuestionId == questionId && v.Vote == 1, ct);
            var dislikeCount = await _context.QuestionVotes.CountAsync(v => v.QuestionId == questionId && v.Vote == -1, ct);
            var myVote = vote == 0 ? (int?)null : (vote > 0 ? 1 : -1);

            return new QuestionVoteResponse
            {
                QuestionId = questionId,
                LikeCount = likeCount,
                DislikeCount = dislikeCount,
                HelpfulPercent = HelpfulPercent(likeCount, dislikeCount),
                MyVote = myVote,
            };
        }

        private static int HelpfulPercent(int like, int dislike)
        {
            var total = like + dislike;
            return total == 0 ? 0 : (int)Math.Round(like * 100.0 / total);
        }
    }
}
