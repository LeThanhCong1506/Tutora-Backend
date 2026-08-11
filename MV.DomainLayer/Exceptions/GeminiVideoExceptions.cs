namespace MV.DomainLayer.Exceptions
{
    /// <summary>Video vượt giới hạn dung lượng Gemini File API (~2GB/file) — không thử upload.</summary>
    public class GeminiVideoTooLargeException : BadRequestException
    {
        public GeminiVideoTooLargeException()
            : base("Video buổi học quá lớn để tóm tắt tự động.") { }
    }

    /// <summary>Upload lỗi, hoặc Gemini báo file xử lý thất bại (state = FAILED), hoặc chờ ACTIVE quá lâu.</summary>
    public class GeminiFileProcessingException : BadRequestException
    {
        public GeminiFileProcessingException(string message) : base(message) { }
    }

    /// <summary>Gemini trả về mã lỗi HTTP không thành công (rate limit, key sai, model lỗi...).</summary>
    public class GeminiApiException : BadRequestException
    {
        public int StatusCode { get; }

        public GeminiApiException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>Response của Gemini rỗng, thiếu candidates, hoặc không parse được đúng định dạng mong đợi.</summary>
    public class GeminiResponseParseException : BadRequestException
    {
        public GeminiResponseParseException(string message) : base(message) { }
    }
}
