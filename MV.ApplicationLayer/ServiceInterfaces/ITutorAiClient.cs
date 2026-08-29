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
        Task<List<AiRankedTutor>?> RankAsync(
            string? query,
            IReadOnlyList<string> candidateIds,
            int topK,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Embed 1 đoạn text (đề+lời giải) thành vector(768) qua tutora-ai /api/v1/embed.
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

        // (nội dung/chính sách upload
        Task<KbUploadResult?> KbUploadAsync(
            byte[] fileBytes, string fileName, string? uploadedBy,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sửa nội dung tài liệu KB — cần AI để chunk lại + re-embed text mới (khác list/
        /// delete). Trả số đoạn mới, hoặc null nếu tutora-ai lỗi/không đọc được nội dung.
        /// </summary>
        Task<int?> KbUpdateContentAsync(string documentId, string content, CancellationToken cancellationToken = default);

        /// <summary>
        /// Bài tương tự để LUYỆN TẬP, tìm bằng embedding (tutora-ai /similar-questions).
        /// </summary>
        Task<List<AiSimilarQuestion>> FindSimilarQuestionsAsync(
            string text,
            string? chapter,
            string? difficulty,
            IReadOnlyList<Guid> excludeIds,
            int topK,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Trích TOÀN VĂN 1 tài liệu học tập (pdf/ảnh) qua tutora-ai
        /// /api/v1/materials/extract. Chạy NGẦM lúc upload để lúc gia sư bấm "Tạo câu
        /// hỏi" giữa buổi dạy không phải chờ parse file.
        /// Trả null nếu tutora-ai lỗi hoặc không đọc được nội dung.
        /// </summary>
        Task<AiMaterialExtraction?> ExtractMaterialAsync(
            byte[] fileBytes, string fileName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sinh bộ câu hỏi từ toàn văn tài liệu + yêu cầu của gia sư
        /// (tutora-ai /api/v1/practice/generate).
        /// Trả null nếu AI lỗi/không sinh được câu nào hợp lệ.
        /// </summary>
        Task<AiGeneratedPractice?> GeneratePracticeAsync(
            IReadOnlyList<AiMaterialSource> materials,
            string prompt,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Toàn văn đã trích của 1 tài liệu — có mốc "[trang N]" giữa các trang.</summary>
    public record AiMaterialExtraction(string FullText, int? PageCount);

    /// <summary>1 tài liệu nguồn đưa vào prompt sinh đề.</summary>
    public record AiMaterialSource(int MaterialId, string Title, string FullText);

    /// <summary>Kết quả AI sinh đề: tiêu đề gợi ý + danh sách câu.</summary>
    public record AiGeneratedPractice(string Title, List<AiGeneratedQuestion> Questions);

    /// <summary>
    /// 1 câu AI sinh ra. Format 'mc' phải có Options + CorrectAnswer; 'essay' thì không.
    /// SourceMaterialId/SourcePage để hiện "Trích từ ... trang N".
    /// </summary>
    public record AiGeneratedQuestion(
        string Format,
        string Content,
        List<AiAnswerOption>? Options,
        string? CorrectAnswer,
        string? Explanation,
        int? SourceMaterialId,
        int? SourcePage);

    public record AiAnswerOption(string Key, string Text);

    public record AiRankedTutor(string TutorId, float Similarity);

    /// <summary>1 bài tương tự lấy từ question bank để luyện tập.</summary>
    public record AiSimilarQuestion(
        Guid Id, string Content, string? Solution,
        string? Chapter, string? Difficulty, float Similarity);

    public record AiExtractedQuestion(
        string Content,
        string? Solution,
        string? ProblemType,
        string? Chapter,
        int? Page,
        List<string> Images);

    public record KbUploadResult(string DocumentId, int ChunkCount, string FileName);
}
