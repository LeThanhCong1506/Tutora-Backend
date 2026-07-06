namespace MV.DomainLayer.Constants;

public static class ApiMessages
{
    public const string Success = "Success";
    public const string SuccessWithExclamation = "Success!";
    public const string Unauthorized = "Unauthorized";
    public const string Forbidden = "Forbidden";
    public const string UserNotAuthenticated = "User not authenticated.";
    public const string InvalidRequestPayload = "Invalid request payload.";
    public const string InvalidInputData = "Invalid input data.";
    public const string InvalidRequestData = "Invalid request data";
    public const string GenericErrorPrefix = "An error occurred: ";
    public const string BookingNotFound = "Booking not found";
    public const string ClassSessionNotFound = "Class session not found";
    public const string TutorProfileNotFound = "Tutor profile not found";
    public const string UserNotFound = "User not found";
    public const string UserNotFoundWithPeriod = "User not found.";
    public const string NotStudentOwner = "NOT_STUDENT_OWNER: Student does not belong to this parent.";
    public const string AdminUserIdNotFound = "Admin user ID not found";
    public const string ActorUserIdNotFound = "User ID not found.";
    public const string SupabaseBaseUrlNotConfigured = "Supabase Base URL is not configured.";
    public const string Unknown = "Unknown";
}
