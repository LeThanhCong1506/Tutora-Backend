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

    /// <summary>Most booked.</summary>
    public const string Popularity     = "popularity";

    /// <summary>
    /// Default sort order. Booking volume leads because it is earned demand — a brand-new profile
    /// can still show a 5.0 average off a couple of reviews, which is what used to top the list.
    /// </summary>
    public const string Default = Popularity;

    /// <remarks>
    /// Wider than the dropdown the search page offers: ExperienceDesc/ReviewsDesc/RatingAsc are no
    /// longer listed there but stay accepted so saved links and older clients keep working.
    /// </remarks>
    public static readonly string[] ValidValues = new[]
    {
        RatingDesc, RatingAsc, PriceAsc, PriceDesc, ExperienceDesc, ReviewsDesc, Newest, Popularity
    };
}
