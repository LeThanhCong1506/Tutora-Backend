namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Client for calling the external Tutor AI (FastAPI) ranking service.
    /// </summary>
    public interface ITutorAiClient
    {
        /// <summary>
        /// Rank a list of candidate tutor IDs using AI semantic similarity.
        /// Returns the ranked IDs with similarity scores.
        /// </summary>
        /// <param name="query">Free-text user query (may be null — AI ranks by rating)</param>
        /// <param name="candidateIds">Pre-filtered tutor IDs from SQL hard filter</param>
        /// <param name="topK">How many results to return</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Ranked list of (TutorId, Similarity), or null on failure (graceful degrade)</returns>
        Task<List<AiRankedTutor>?> RankAsync(
            string? query,
            IReadOnlyList<string> candidateIds,
            int topK,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Embed 1 đoạn text (đề+lời giải) thành vector(768) qua tutora-ai /api/v1/embed.
        /// Trả về vector, hoặc null nếu embed lỗi (câu hỏi vẫn được lưu, embed lại sau).
        /// </summary>
        Task<float[]?> EmbedAsync(string id, string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gửi PDF cho tutora-ai (/api/v1/extract-pdf) -> AI đọc, tách list câu hỏi.
        /// Trả về danh sách câu (đề+lời giải+chương+trang), hoặc null nếu lỗi.
        /// </summary>
        Task<List<AiExtractedQuestion>?> ExtractPdfAsync(
            byte[] pdfBytes, string fileName, CancellationToken cancellationToken = default);
    }

    public record AiRankedTutor(string TutorId, float Similarity);

    public record AiExtractedQuestion(
        string Content,
        string? Solution,
        string? ProblemType,
        string? Chapter,
        int? Page);
}
