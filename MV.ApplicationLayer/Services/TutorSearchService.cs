using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Service implementation for tutor search operations
    /// Matches Figma UI design for tutor search page
    /// </summary>
    public class TutorSearchService : ITutorSearchService
    {
        private readonly ITutorSearchRepository _tutorSearchRepository;

        public TutorSearchService(ITutorSearchRepository tutorSearchRepository)
        {
            _tutorSearchRepository = tutorSearchRepository;
        }

        public async Task<TutorSearchPagedResponse> SearchTutorsAsync(TutorSearchParameters parameters)
        {
            // Validate parameters
            ValidateSearchParameters(parameters);

            // Execute search through repository
            var result = await _tutorSearchRepository.SearchTutorsAsync(parameters);

            return result;
        }

        #region Private Helper Methods

        private static void ValidateSearchParameters(TutorSearchParameters parameters)
        {
            // Page size validation
            if (parameters.PageNumber < 1)
                parameters.PageNumber = 1;

            // Money range validation
            if (parameters.MinHourlyRate.HasValue && parameters.MaxHourlyRate.HasValue)
            {
                if (parameters.MinHourlyRate > parameters.MaxHourlyRate)
                {
                    (parameters.MinHourlyRate, parameters.MaxHourlyRate) =
                        (parameters.MaxHourlyRate, parameters.MinHourlyRate);
                }
            }

            // Rating validation - ensure it's between 0 and 5
            if (parameters.MinRating.HasValue)
            {
                parameters.MinRating = Math.Clamp(parameters.MinRating.Value, 0, 5);
            }

            // Sanitize search term (Category, GradeLevel, TeachingMode, BudgetRange, SortBy)
            if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
            {
                parameters.SearchTerm = parameters.SearchTerm.Trim();
                // Limit search term length to prevent abuse
                if (parameters.SearchTerm.Length > 100)
                    parameters.SearchTerm = parameters.SearchTerm.Substring(0, 100);
            }

            // Validate category
            if (!string.IsNullOrWhiteSpace(parameters.Category))
            {
                if (!TutorSearchCategory.ValidValues.Contains(parameters.Category.ToLower()))
                    parameters.Category = TutorSearchCategory.AllValue;
            }

            // Validate grade level - accept standard values OR db-aligned values like "Grade_1", "IELTS_7.0"
            if (!string.IsNullOrWhiteSpace(parameters.GradeLevel))
            {
                var validPrefixes = TutorSearchGradeLevel.ValidPrefixes;
                var lower = parameters.GradeLevel.ToLower();

                // Check if it starts with any valid prefix or contains legacy words
                if (!validPrefixes.Any(p => lower.StartsWith(p)) && !lower.Contains("lớp") && !lower.Contains("lop"))
                {
                    parameters.GradeLevel = null; // Invalid value
                }
            }

            // Validate teaching mode
            if (!string.IsNullOrWhiteSpace(parameters.TeachingMode))
            {
                var validModes = new[] { TeachingMode.Online, TeachingMode.Offline, TeachingMode.Hybrid };
                if (!validModes.Contains(parameters.TeachingMode.ToLower()))
                    parameters.TeachingMode = null;
            }

            // Validate budget range
            if (!string.IsNullOrWhiteSpace(parameters.BudgetRange))
            {
                if (!TutorSearchBudgetRange.ValidValues.Contains(parameters.BudgetRange.ToLower()))
                    parameters.BudgetRange = TutorSearchBudgetRange.AllValue;
            }

            // Validate sort by
            if (!string.IsNullOrWhiteSpace(parameters.SortBy))
            {
                if (!TutorSearchSortBy.ValidValues.Contains(parameters.SortBy.ToLower()))
                    parameters.SortBy = TutorSearchSortBy.Default;
            }
        }

        #endregion
    }
}
