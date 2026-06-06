using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO
{
    // 1. Lớp cơ sở (Non-generic): Dùng để trả về thông báo lỗi chung chung hoặc thành công không kèm dữ liệu
    public class APIResponse
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Error { get; set; }

        public APIResponse(int statusCode, string message)
        {
            StatusCode = statusCode;
            Message = message;
        }

        public APIResponse(int statusCode, string message, object? error)
        {
            StatusCode = statusCode;
            Message = message;
            Error = error;
        }

        // Helper trả về lỗi (mặc định trả về object null)
        public static APIResponse<object> Fail(string message, int statusCode = 400)
        {
            return new APIResponse<object>(statusCode, message, null);
        }

        public static APIResponse<object> Fail(string message, int statusCode, object? error)
        {
            return new APIResponse<object>(statusCode, message, null, error);
        }

        // Helper trả về thành công (không data)
        public static APIResponse<object> Success(string message = ApiMessages.Success, int statusCode = 200)
        {
            return new APIResponse<object>(statusCode, message, null);
        }
    }

    // 2. Lớp Generic (Kế thừa lớp trên): Dùng khi API trả về dữ liệu cụ thể T (ví dụ User, FptAiResult...)
    public class APIResponse<T> : APIResponse
    {
        public T? Content { get; set; }

        public APIResponse(int statusCode, string message, T? content)
            : base(statusCode, message)
        {
            Content = content;
        }

        public APIResponse(int statusCode, string message, T? content, object? error)
            : base(statusCode, message, error)
        {
            Content = content;
        }

        // Quan trọng: Dùng từ khóa 'new' để ẩn hàm Fail của lớp cha.
        // Hàm này trả về đúng kiểu APIResponse<T> giúp sửa lỗi "Cannot implicitly convert..."
        public static new APIResponse<T> Fail(string message, int statusCode = 400)
        {
            return new APIResponse<T>(statusCode, message, default);
        }

        public static new APIResponse<T> Fail(string message, int statusCode, object? error)
        {
            return new APIResponse<T>(statusCode, message, default, error);
        }

        public static APIResponse<T> Success(T content, string message = ApiMessages.SuccessWithExclamation, int statusCode = 200)
        {
            return new APIResponse<T>(statusCode, message, content);
        }
    }
}
