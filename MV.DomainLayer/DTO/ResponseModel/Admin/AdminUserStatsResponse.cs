namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Top-level response for GET /api/admin/dashboard/users
/// </summary>
public class AdminUserStatsResponse
{
    public UserStatsByRole ByRole { get; set; } = new();
    public UserStatsTutorFunnel TutorFunnel { get; set; } = new();
    public UserStatsGrowth Growth { get; set; } = new();
    public UserStatsModeration Moderation { get; set; } = new();
    public DateTime? FilterFrom { get; set; }
    public DateTime? FilterTo { get; set; }
}

public class UserStatsByRole
{
    public int TotalTutors { get; set; }
    public int TotalParents { get; set; }
    public int TotalStudents { get; set; }
    public int TotalStaff { get; set; }
}

public class UserStatsTutorFunnel
{
    public int Draft { get; set; }
    public int PendingApproval { get; set; }
    public int Active { get; set; }
    public int Rejected { get; set; }
    public int PublicTutors { get; set; }
}

public class UserStatsGrowth
{
    public int NewTutors { get; set; }
    public int NewParents { get; set; }
    public int NewStudents { get; set; }
    public int NewUsersThisWeek { get; set; }
    public int NewUsersThisMonth { get; set; }
}

public class UserStatsModeration
{
    public int ActiveSuspensions { get; set; }
    public int TotalWarnings { get; set; }
    public int UsersWithWarnings { get; set; }
}
