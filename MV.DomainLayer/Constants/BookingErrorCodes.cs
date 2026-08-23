namespace MV.DomainLayer.Constants;

public static class BookingErrorCodes
{
    public const string InvalidInput = "INVALID_INPUT";
    public const string InvalidPackageType = "INVALID_PACKAGE_TYPE";
    public const string InvalidSchedule = "INVALID_SCHEDULE";
    public const string InvalidStartDate = "INVALID_START_DATE";
    public const string SlotInPast = "SLOT_IN_PAST";
    public const string LocationRequired = "LOCATION_REQUIRED";
    public const string NotStudentOwner = "NOT_STUDENT_OWNER";
    // Tài khoản học sinh do phụ huynh tạo/quản lý → không được tự đặt lịch.
    public const string StudentManagedByParent = "STUDENT_MANAGED_BY_PARENT";
    // Học sinh tự đăng ký chưa xác minh CCCD hoặc dưới 16 tuổi → không được đặt lịch.
    public const string StudentIdentityNotVerified = "STUDENT_IDENTITY_NOT_VERIFIED";
    public const string StudentUnderage = "STUDENT_UNDERAGE";
    public const string TutorNotFound = "TUTOR_NOT_FOUND";
    public const string SubjectNotFound = "SUBJECT_NOT_FOUND";
    public const string TutorNotAvailable = "TUTOR_NOT_AVAILABLE";
    public const string TutorNotTeachSubject = "TUTOR_NOT_TEACH_SUBJECT";
    public const string SubjectOrGradeLevelInactive = "SUBJECT_GRADE_LEVEL_INACTIVE";
    public const string PromotionInvalid = "PROMOTION_INVALID";
    public const string PromotionMinOrder = "PROMOTION_MIN_ORDER";
    public const string PromotionCodeExists = "PROMOTION_CODE_EXISTS";
    public const string BookingNotFound = "BOOKING_NOT_FOUND";
    public const string NotBookingOwner = "NOT_BOOKING_OWNER";
    public const string InvalidBookingStatus = "INVALID_BOOKING_STATUS";
    public const string InvalidWebhookPayload = "INVALID_WEBHOOK_PAYLOAD";
    public const string InvalidSignature = "INVALID_SIGNATURE";
    public const string BookingAlreadyPaid = "BOOKING_ALREADY_PAID";
    public const string AmountMismatch = "AMOUNT_MISMATCH";
    public const string BookingExpired = "BOOKING_EXPIRED";
    public const string InvalidTeachingMode = "INVALID_TEACHING_MODE";
    public const string ScheduleNotInAvailability = "SCHEDULE_NOT_IN_AVAILABILITY";
    public const string ScheduleConflict = "SCHEDULE_CONFLICT";
    public const string DepositAlreadyPaid = "DEPOSIT_ALREADY_PAID";
    public const string RemainingAlreadyPaid = "REMAINING_ALREADY_PAID";
    public const string RemainingNotPaid = "REMAINING_NOT_PAID";
    public const string DuplicateTransaction = "DUPLICATE_TRANSACTION";
}
