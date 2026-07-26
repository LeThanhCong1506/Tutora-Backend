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
        /// Vector hoá 1 gia sư — gọi tutora-ai POST /api/v1/tutors/{id}/embed khi hồ sơ/giá
        /// đổi hoặc được duyệt.
        /// </summary>
        Task EmbedTutorAsync(string tutorId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gửi PDF cho tutora-ai (/api/v1/extract-pdf) -> AI đọc, tách list câu hỏi.
        /// </summary>
        Task<List<AiExtractedQuestion>?> ExtractPdfAsync(
            byte[] pdfBytes, string fileName, CancellationToken cancellationToken = default);

        // Knowledge Base (nội dung/chính sách Tutora — CEO upload từ CMS)
        Task<KbUploadResult?> KbUploadAsync(
            byte[] fileBytes, string fileName, string? uploadedBy,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách tài liệu KB đã nạp (/api/v1/kb/documents).
        /// </summary>
        Task<List<KbDocument>?> KbListDocumentsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Xoá 1 tài liệu KB + toàn bộ chunk (/api/v1/kb/documents/{id}).
        /// </summary>
        Task<bool> KbDeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    }

    public record AiRankedTutor(string TutorId, float Similarity);

    public record AiExtractedQuestion(
        string Content,
        string? Solution,
        string? ProblemType,
        string? Chapter,
        int? Page,
        List<string> Images);

    public record KbUploadResult(string DocumentId, int ChunkCount, string FileName);

    public record KbDocument(
        string Id,
        string FileName,
        string SourceType,
        int ChunkCount,
        string Status,
        DateTime? CreatedAt);
}
