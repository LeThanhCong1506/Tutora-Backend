namespace MV.DomainLayer.Exceptions;

public class InvalidBankInfoException : Exception
{
    public string ErrorCode { get; }

    public InvalidBankInfoException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public InvalidBankInfoException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public class BankVerificationException : Exception
{
    public string ErrorCode { get; }

    public BankVerificationException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public BankVerificationException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public class RateLimitExceededException : Exception
{
    public RateLimitExceededException(string message) : base(message)
    {
    }
}

// Micro-deposit specific exceptions (M4-T2)
public class VerificationExpiredException : Exception
{
    public VerificationExpiredException(string message) : base(message)
    {
    }
}

public class DepositFailedException : Exception
{
    public string ErrorCode { get; }

    public DepositFailedException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public DepositFailedException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public class AlreadyVerifiedException : Exception
{
    public AlreadyVerifiedException(string message) : base(message)
    {
    }
}

public class ExternalApiException : Exception
{
    public ExternalApiException(string message) : base(message)
    {
    }

    public ExternalApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
