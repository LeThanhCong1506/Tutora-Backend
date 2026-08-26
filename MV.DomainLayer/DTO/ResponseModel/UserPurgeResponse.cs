namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Whether an account can be erased for good, and the exact sentence the operator must type
/// to prove they mean it.
/// </summary>
public class UserPurgePreflightResponse
{
    public string UserId { get; set; } = null!;
    public string? FullName { get; set; }
    public string? Role { get; set; }

    /// <summary>True when nothing is left that must outlive the account.</summary>
    public bool CanPurge { get; set; }

    /// <summary>Why not, in words the operator can act on. Empty when <see cref="CanPurge"/>.</summary>
    public List<string> Blockers { get; set; } = new();

    /// <summary>
    /// The sentence the operator has to type back, naming both themselves and the target so it
    /// cannot be muscle-memory-confirmed on the wrong row. Generated and re-checked server-side.
    /// </summary>
    public string ConfirmationPhrase { get; set; } = null!;

    /// <summary>What the erase will destroy, so the count is seen before the click, not after.</summary>
    public UserPurgeFootprint Footprint { get; set; } = new();
}

/// <summary>Rows tied to the account, counted before deletion.</summary>
public class UserPurgeFootprint
{
    public int Bookings { get; set; }
    public int ClassSessions { get; set; }
    public int WalletTransactions { get; set; }
    public int Feedbacks { get; set; }
    public int Disputes { get; set; }
    public int ChatMessages { get; set; }
    public int Warnings { get; set; }
}

/// <summary>Outcome of an erase that actually ran.</summary>
public class UserPurgeResultResponse
{
    public string UserId { get; set; } = null!;
    public string? FullName { get; set; }
    public UserPurgeFootprint Deleted { get; set; } = new();
    public DateTime PurgedAt { get; set; }
    public string? PurgedByName { get; set; }
}
