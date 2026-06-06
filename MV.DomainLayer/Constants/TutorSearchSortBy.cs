namespace MV.DomainLayer.Constants;

/// <summary>
/// Tutor search sort-by option constants.
/// </summary>
public static class TutorSearchSortBy
{
    public const string RatingDesc     = "rating_desc";
    public const string RatingAsc      = "rating_asc";
    public const string PriceAsc       = "price_asc";
    public const string PriceDesc      = "price_desc";
    public const string ExperienceDesc = "experience_desc";
    public const string ReviewsDesc    = "reviews_desc";
    public const string Newest         = "newest";
    public const string Popularity     = "popularity";

    /// <summary>Default sort order.</summary>
    public const string Default = RatingDesc;

    public static readonly string[] ValidValues = new[]
    {
        RatingDesc, RatingAsc, PriceAsc, PriceDesc, ExperienceDesc, ReviewsDesc, Newest, Popularity
    };
}
