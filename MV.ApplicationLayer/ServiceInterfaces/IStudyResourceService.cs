using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Question;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Trang Tài nguyên công khai (study-resources): list câu hỏi mẫu đã published
    /// theo môn/chương + vote like/dislike.
    /// </summary>
    public interface IStudyResourceService
    {
        /// <summary>
        /// Câu hỏi published theo slug môn (bắt buộc) + slug chương (tùy chọn), phân trang.
        /// </summary>
        Task<PagedList<PublicQuestionResponse>?> GetQuestionsAsync(
            string subjectSlug, string? chapterSlug, int pageNumber, int pageSize,
            string? userId, CancellationToken ct = default);

        /// <summary>
        /// Chi tiết 1 câu published theo id.
        /// </summary>
        Task<PublicQuestionResponse?> GetByIdAsync(Guid questionId, string? userId, CancellationToken ct = default);

        /// <summary>
        /// Vote 1 câu: vote = 1 (like) | -1 (dislike) | 0 (bỏ vote). Upsert theo (question, user).
        /// </summary>
        Task<QuestionVoteResponse?> VoteAsync(
            Guid questionId, string userId, int vote, CancellationToken ct = default);
    }
}
