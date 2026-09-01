using MV.DomainLayer.Constants;

namespace MV.DomainLayer.Exceptions
{
    public abstract class BadRequestException : Exception
    {
        protected BadRequestException(string message) : base(message) { }
    }

    public abstract class NotFoundException : Exception
    {
        protected NotFoundException(string message) : base(message) { }
    }

    public class UserNotFoundException : NotFoundException
    {
        public UserNotFoundException(string userId)
            : base($"User with ID '{userId}' was not found.") { }

        // Constructor không tham số nếu cần
        public UserNotFoundException() : base(ApiMessages.UserNotFoundWithPeriod) { }
    }

    // Email Exceptions
    public class EmailAlreadyExistsException : BadRequestException
    {
        public EmailAlreadyExistsException()
            : base("The provided email address is already exist.")
        {
        }
    }

    public class EmailNotFoundException : NotFoundException
    {
        public EmailNotFoundException()
            : base("Email address was not found.")
        {
        }
    }

    // UserName Exceptions
    public class UsernameAlreadyExistsException : BadRequestException
    {
        public UsernameAlreadyExistsException()
            : base("The provided username is already exist.")
        {
        }
    }

    public class UsernameNotFoundException : NotFoundException
    {
        public UsernameNotFoundException()
            : base("Username was not found.")
        {
        }
    }

    // Phone Number Exceptions
    public class PhoneAlreadyExistsException : BadRequestException
    {
        public PhoneAlreadyExistsException()
            : base("The provided phone number is already exist.")
        {
        }
    }

    public class PhoneNotFoundException : NotFoundException
    {
        public PhoneNotFoundException()
            : base("Phone number was not found.")
        {
        }
    }

    // Identity Number Exceptions
    public class IdentityNumberAlreadyExistsException : BadRequestException
    {
        public IdentityNumberAlreadyExistsException()
            : base("The provided identity number already exists.")
        {
        }
    }

    public class IdentityNumberNotFoundException : NotFoundException
    {
        public IdentityNumberNotFoundException()
            : base("Identity number was not found.")
        {
        }
    }

    public class BookingException : Exception
    {
        public string ErrorCode { get; }
        public int HttpStatus { get; }

        public BookingException(string errorCode, string message, int httpStatus = 400) : base(message)
        {
            ErrorCode = errorCode;
            HttpStatus = httpStatus;
        }
    }

    // Chat Exceptions
    public class ChannelNotFoundException : NotFoundException
    {
        public ChannelNotFoundException(int channelId)
            : base($"Chat channel with ID '{channelId}' was not found.") { }
    }

    public class NotChannelParticipantException : BadRequestException
    {
        public NotChannelParticipantException()
            : base("You are not a participant in this chat channel.") { }
    }

    public class InvalidMessageException : BadRequestException
    {
        public InvalidMessageException(string message)
            : base(message) { }
    }

    // AI Chat Exceptions
    public class AiChatSessionNotFoundException : NotFoundException
    {
        public AiChatSessionNotFoundException(Guid sessionId)
            : base($"AI chat session with ID '{sessionId}' was not found.") { }
    }

    public class AiChatSessionForbiddenException : BadRequestException
    {
        public AiChatSessionForbiddenException()
            : base("Bạn không có quyền truy cập phiên trò chuyện AI này.") { }
    }

    public class QuestionNoteNotFoundException : NotFoundException
    {
        public QuestionNoteNotFoundException(Guid noteId)
            : base($"Question note with ID '{noteId}' was not found.") { }
    }

    public class QuestionNoteForbiddenException : BadRequestException
    {
        public QuestionNoteForbiddenException()
            : base("Bạn không có quyền truy cập note này.") { }
    }

    // Finance Exceptions
    public class WalletNotFoundException : NotFoundException
    {
        public WalletNotFoundException()
            : base("Không tìm thấy ví của bạn.") { }
    }

    public class InsufficientBalanceException : BadRequestException
    {
        public InsufficientBalanceException()
            : base("Số dư khả dụng không đủ để thực hiện giao dịch này.") { }
    }

    public class PendingWithdrawalException : BadRequestException
    {
        public PendingWithdrawalException()
            : base("Bạn đang có một yêu cầu rút tiền khác chưa được xử lý xong.") { }
    }

    public class BankInfoRequiredException : BadRequestException
    {
        public BankInfoRequiredException()
            : base("Vui lòng cập nhật thông tin ngân hàng trước.") { }
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

    public class WithdrawalAmountTooLowException : BadRequestException
    {
        public WithdrawalAmountTooLowException(decimal minAmount)
            : base($"Số tiền rút tối thiểu là {minAmount:N0} VND.") { }
    }

    public class WithdrawalNotFoundException : NotFoundException
    {
        public WithdrawalNotFoundException()
            : base("Withdrawal request not found.") { }
    }

    public class WithdrawalCancellationException : BadRequestException
    {
        public WithdrawalCancellationException()
            : base("Withdrawal requests must be cancelled by staff after transfer status is verified.") { }
    }

    public class TutorProfileNotFoundException : NotFoundException
    {
        public TutorProfileNotFoundException()
            : base("Tutor profile not found.") { }
    }

    public class TransactionNotFoundException : NotFoundException
    {
        public TransactionNotFoundException()
            : base("Transaction not found.") { }
    }

    // Learning Material Exceptions
    public class BookingNotFoundException : NotFoundException
    {
        public BookingNotFoundException()
            : base("Không tìm thấy booking.") { }
    }

    public class MaterialNotFoundException : NotFoundException
    {
        public MaterialNotFoundException()
            : base("Không tìm thấy tài liệu.") { }
    }

    public class MaterialAccessDeniedException : BadRequestException
    {
        public MaterialAccessDeniedException()
            : base("Bạn không có quyền truy cập tài liệu của booking này.") { }
    }

    /// <summary>Tài liệu không thuộc môn đang dạy (vd tải nhầm CV lên lớp Toán).</summary>
    public class MaterialNotRelevantException : BadRequestException
    {
        public MaterialNotRelevantException(string? reason, string? subject)
            : base(string.IsNullOrWhiteSpace(reason)
                ? $"Tài liệu này không thuộc môn {subject ?? "đang dạy"}. Vui lòng chọn đúng học liệu."
                : reason) { }
    }

    // Bài tập nhanh trong buổi họcz

    public class PracticeSetNotFoundException : NotFoundException
    {
        public PracticeSetNotFoundException()
            : base("Không tìm thấy bộ bài tập.") { }
    }

    public class PracticeQuestionNotFoundException : NotFoundException
    {
        public PracticeQuestionNotFoundException()
            : base("Không tìm thấy câu hỏi.") { }
    }

    /// <summary>Chỉ gia sư của booking mới được tạo/sửa/gửi bài tập.</summary>
    public class PracticeAccessDeniedException : BadRequestException
    {
        public PracticeAccessDeniedException()
            : base("Bạn không có quyền thao tác với bài tập của buổi học này.") { }
    }

    /// <summary>Bộ đã gửi thì không sửa/xoá được nữa — học sinh có thể đang làm dở.</summary>
    public class PracticeSetAlreadySentException : BadRequestException
    {
        public PracticeSetAlreadySentException()
            : base("Bộ bài tập đã gửi cho học sinh, không sửa được nữa.") { }
    }

    /// <summary>Học sinh chỉ làm được bài đã gửi.</summary>
    public class PracticeSetNotSentException : BadRequestException
    {
        public PracticeSetNotSentException()
            : base("Bài tập này chưa được gia sư gửi.") { }
    }

    public class MaterialContentNotReadyException : BadRequestException
    {
        public MaterialContentNotReadyException(string materialTitle)
            : base($"Tài liệu \"{materialTitle}\" chưa xử lý xong nội dung, chưa dùng để tạo câu hỏi được.") { }
    }

    public class PracticeQuestionInvalidException : BadRequestException
    {
        public PracticeQuestionInvalidException(string message) : base(message) { }
    }

    public class PracticeSetEmptyException : BadRequestException
    {
        public PracticeSetEmptyException()
            : base("Bộ bài tập chưa có câu hỏi nào.") { }
    }

    /// <summary>AI từ chối yêu cầu (lạc đề, chat chit, đòi lộ prompt...) — kèm lý do.</summary>
    public class PracticeGenerationRefusedException : BadRequestException
    {
        public PracticeGenerationRefusedException(string reason) : base(reason) { }
    }

    /// <summary>Đã dùng hết hạn mức câu hỏi của buổi học (tính năng thử nghiệm).</summary>
    public class PracticeQuotaExceededException : BadRequestException
    {
        public PracticeQuotaExceededException(int used, int max)
            : base($"Buổi học này đã tạo {used}/{max} câu hỏi — hết hạn mức của tính năng "
                   + "thử nghiệm. Xoá bớt câu chưa dùng hoặc để dành cho buổi sau nhé.") { }
    }

    public class PracticeGenerationFailedException : BadRequestException
    {
        public PracticeGenerationFailedException()
            : base("Chưa tạo được câu hỏi từ tài liệu này. Bạn thử mô tả rõ hơn hoặc chọn tài liệu khác nhé.") { }
    }
}
