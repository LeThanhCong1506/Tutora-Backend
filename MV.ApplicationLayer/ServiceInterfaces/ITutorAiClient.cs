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
    }

    public record AiRankedTutor(string TutorId, float Similarity);
}
