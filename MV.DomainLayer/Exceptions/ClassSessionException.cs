namespace MV.DomainLayer.Exceptions;

/// <summary>
/// Exception for classSession-related errors
/// </summary>
public class ClassSessionException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatus { get; }

    public ClassSessionException(string errorCode, string message, int httpStatus = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
    }
}
