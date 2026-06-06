namespace MV.DomainLayer.Exceptions;

/// <summary>
/// Exception for lesson-related errors
/// </summary>
public class LessonException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatus { get; }

    public LessonException(string errorCode, string message, int httpStatus = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
    }
}
