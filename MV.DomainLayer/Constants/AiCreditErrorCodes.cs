namespace MV.DomainLayer.Constants;

public static class AiCreditErrorCodes
{
    public const string PackageNotFound = "AI_CREDIT_PACKAGE_NOT_FOUND";
    public const string PackageHasTransactions = "AI_CREDIT_PACKAGE_HAS_TRANSACTIONS";
    public const string PackageNotPurchasable = "AI_CREDIT_PACKAGE_NOT_PURCHASABLE";
    public const string PackageCodeExists = "AI_CREDIT_PACKAGE_CODE_EXISTS";
    public const string UserNotFound = "AI_CREDIT_USER_NOT_FOUND";
    public const string InsufficientCredits = "AI_CREDIT_INSUFFICIENT";
    public const string InvalidAmount = "AI_CREDIT_INVALID_AMOUNT";
}

public static class AiCreditSource
{
    public const string Solve = "solve";
    public const string Grant = "grant";
    public const string Purchase = "purchase";
    public const string DailyReset = "daily_reset";
}

public static class AiCreditPackageCode
{
    public const string Free = "free";
    public const string Plus = "plus";
    public const string Pro = "pro";
}

public static class AiCreditConfigKeys
{
    /// <summary>Số lượt AI credit tặng cho học sinh mỗi khi có booking.</summary>
    public const string BonusOnBooking = "ai_credit_bonus_on_booking";

    /// <summary>Số THÁNG credit hết hạn kể từ ngày cấp. Áp cho cả 3 nguồn.</summary>
    public const string ExpiryMonths = "ai_credit_expiry_months";

    /// <summary>Số lượt tặng khi tài khoản mới xác thực SĐT thành công.</summary>
    public const string FreeOnSignup = "ai_credit_free_on_signup";
}

/// <summary>Nguồn của một LÔ credit (ai_credit_batches.source).</summary>
public static class AiCreditBatchSource
{
    public const string FreeSignup = "free_signup";
    public const string BookingBonus = "booking_bonus";
    public const string Purchase = "purchase";
}
