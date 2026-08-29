using System.Net;
using System.Security.Authentication;
using MV.DomainLayer.Constants;
using DomainExceptions = MV.DomainLayer.Exceptions;


namespace SP25.OJT202.AccountManagement.Presentation.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Chuyển tiếp request
                await _next(context);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi
                _logger.LogError(ex, "An unhandled exception occurred.");

                // Xử lý response khi có lỗi
                await HandleExceptionAsync(context, ex);
            }
        }

        /// <summary>
        /// Handles exceptions that occur during the request processing pipeline.
        /// </summary>
        /// <param name="context">The HttpContext for the current request.</param>
        /// <param name="exception">The exception that was thrown.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        /// <remarks>
        /// This method sets the appropriate HTTP status code and message based on the type of exception
        /// </remarks>
        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            int statusCode;
            string message;

            switch (exception)
            {
                // Trong codebase này, InvalidOperationException là KÊNH mang thông điệp nghiệp
                // vụ tiếng Việt cho người dùng — mọi controller có try/catch đều trả thẳng
                // ex.Message (vd "Cần chờ đủ 48h…", "Khung … nằm ngoài lịch rảnh của bạn").
                // Trước đây middleware thay bằng một câu chung chung, nên ~90 endpoint ghi dữ
                // liệu không tự bắt sẽ khiến người dùng không biết mình sai ở đâu.
                //
                // Đánh đổi đã biết: .NET/EF cũng ném loại này cho lỗi kỹ thuật ("Sequence
                // contains no elements", …) và message đó giờ lọt ra ngoài. Chấp nhận vì nó khó
                // hiểu chứ không lộ bí mật, và những ca đó vốn là bug cần sửa. Nếu về sau thấy
                // phiền, hãy tách một BusinessRuleException riêng và chỉ cho loại đó đi qua.
                case InvalidOperationException:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    message = exception.Message;
                    break;
                case AccountLockedException:
                    statusCode = (int)HttpStatusCode.Forbidden;
                    message = "Your account has been locked. Please contact the administrator.";
                    break;
                case UnauthorizedAccessException:
                    statusCode = (int)HttpStatusCode.Unauthorized;
                    message = "Authorized fail, cannot access this feature.";
                    break;
                case ArgumentException:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    message = "Invalid input provided.";
                    break;
                case AuthenticationException:
                    statusCode = (int)HttpStatusCode.Forbidden;
                    message = "Wrong username or password. Please verify your credentials and try again.";
                    break;
                case NullReferenceException:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    message = "Account is null, details are required.";
                    break;
                case UserExistException:
                    statusCode = (int)HttpStatusCode.Conflict;
                    message = "Account already exists.";
                    break;
                case UserNotFoundException:
                    statusCode = (int)HttpStatusCode.NotFound;
                    message = ApiMessages.UserNotFoundWithPeriod;
                    break;
                case DomainExceptions.NotFoundException:
                    statusCode = (int)HttpStatusCode.NotFound;
                    message = exception.Message;
                    break;
                case DomainExceptions.BadRequestException:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    message = exception.Message;
                    break;
                default:
                    statusCode = (int)HttpStatusCode.InternalServerError;
                    message = $"[DEBUG] {exception.GetType().Name}: {exception.Message}";
                    break;
            }

            context.Response.ContentType = HttpConstants.JsonContentType;
            context.Response.StatusCode = statusCode;

            // camelCase để khớp APIResponse (statusCode/message) — FE đọc field lowercase,
            // JsonSerializer.Serialize ở đây không đi qua MVC formatter nên không tự camelCase.
            var response = new
            {
                statusCode = statusCode,
                message = message
            };

            return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
        }
    }

    public class UserExistException : Exception
    {
        public UserExistException() : base()
        {
        }
    }


    public class UserNotFoundException : Exception
    {
        public UserNotFoundException() : base()
        {
        }
    }

    public class AccountLockedException : Exception
    {
        public AccountLockedException() : base()
        {
        }
    }
}
