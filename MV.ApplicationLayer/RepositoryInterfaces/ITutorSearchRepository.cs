using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    /// <summary>
    /// Repository interface for tutor search operations
    /// Supports PostgreSQL Full-Text Search with unaccent and pg_trgm extensions
    /// Matches Figma UI design for tutor search page
    /// </summary>
    public interface ITutorSearchRepository
    {
        /// <summary>
        /// Search and filter tutors with pagination
        /// Uses fuzzy search with Vietnamese unaccent support
        /// </summary>
        /// <param name="parameters">Search and filter parameters</param>
        /// <returns>Paged list of tutor search results</returns>
        Task<TutorSearchPagedResponse> SearchTutorsAsync(TutorSearchParameters parameters);

        /// <summary>
        /// Get filter metadata (available options for filters)
        /// Used to populate filter dropdowns in UI (Categories, GradeLevels, Budget, TeachingModes, SortOptions)
        /// </summary>
        /// <returns>Available filter options with counts</returns>
        Task<TutorSearchFilterMetadata> GetFilterMetadataAsync();

        /// <summary>
        /// Get all available subjects for filter dropdown
        /// </summary>
        /// <returns>List of subjects with tutor counts</returns>
        Task<List<FilterOption>> GetAvailableSubjectsAsync();

        /// <summary>
        /// Get all available cities for filter dropdown
        /// </summary>
        /// <returns>List of cities with tutor counts</returns>
        Task<List<FilterOption>> GetAvailableCitiesAsync();

        /// <summary>
        /// Get available categories for tabs (Khoa học, Ngôn ngữ, Nghệ thuật, IT & Tech)
        /// </summary>
        /// <returns>List of categories with tutor counts</returns>
        Task<List<FilterOption>> GetAvailableCategoriesAsync();

        /// <summary>
        /// Get available grade levels for filter dropdown
        /// </summary>
        /// <returns>List of grade levels</returns>
        Task<List<FilterOption>> GetAvailableGradeLevelsAsync();
    }
}
