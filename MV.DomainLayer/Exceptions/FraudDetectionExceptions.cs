namespace MV.DomainLayer.Exceptions;

public class FraudCheckFailedException : BookingException
{
    public List<string> FailedRules { get; }

    public FraudCheckFailedException(string message, string errorCode, List<string> failedRules)
        : base(errorCode, message, 400)
    {
        FailedRules = failedRules;
    }
}

public class SuspiciousActivityException : BookingException
{
    public List<string> FlaggedRules { get; }

    public SuspiciousActivityException(string message, string errorCode, List<string> flaggedRules)
        : base(errorCode, message, 200)
    {
        FlaggedRules = flaggedRules;
    }
}
