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
            : base("Wallet not found.") { }
    }

    public class InsufficientBalanceException : BadRequestException
    {
        public InsufficientBalanceException()
            : base("Insufficient balance.") { }
    }

    public class PendingWithdrawalException : BadRequestException
    {
        public PendingWithdrawalException()
            : base("You have a pending withdrawal request.") { }
    }

    public class BankInfoRequiredException : BadRequestException
    {
        public BankInfoRequiredException()
            : base("Please update your bank information first.") { }
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
}
