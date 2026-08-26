namespace MV.DomainLayer.Entities;

/// <summary>
/// A tutor one account saved to their wishlist. One row per (user, tutor) pair — un-saving
/// deletes the row rather than flagging it, so the table only ever holds current favourites.
/// </summary>
public partial class TutorFavorite
{
    public long Favoriteid { get; set; }

    public string Userid { get; set; } = null!;

    /// <summary>References <c>tutor_profiles.tutor_id</c>, which is the tutor's user id.</summary>
    public string Tutorid { get; set; } = null!;

    public DateTime Createdat { get; set; }

    public virtual User? User { get; set; }

    public virtual Tutorprofile? TutorProfile { get; set; }
}
