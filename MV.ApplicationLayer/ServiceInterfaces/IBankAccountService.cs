using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Tài khoản ngân hàng dùng chung cho Tutor/Parent/Student tự đăng ký (bảng <c>bank_accounts</c>,
/// 1-1 theo user). Mọi thao tác ghi (lưu/xoá) bắt buộc qua OTP — xem <see cref="IBankAccountOtpService"/>.
/// </summary>
public interface IBankAccountService
{
    Task<BankAccountResponse> GetAsync(string userId, CancellationToken ct = default);

    /// <summary>Gửi OTP tới SĐT riêng đã xác thực của user. Ném exception nếu chưa xác thực SĐT
    /// hoặc là học sinh do phụ huynh quản lý (không thuộc diện tự quản lý tài khoản ngân hàng).</summary>
    Task SendOtpAsync(string userId, CancellationToken ct = default);

    Task VerifyOtpAsync(string userId, string code, CancellationToken ct = default);

    /// <summary>
    /// Lưu (thêm mới hoặc sửa) tài khoản ngân hàng. Ném <see cref="MV.DomainLayer.Exceptions.BankAccountOtpRequiredException"/>
    /// nếu chưa có xác nhận OTP còn hiệu lực cho user này.
    /// </summary>
    Task<BankAccountResponse> SaveAsync(string userId, SaveBankAccountRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default);

    /// <summary>Xoá tài khoản ngân hàng đã lưu — cùng yêu cầu OTP như lưu.</summary>
    Task DeleteAsync(string userId, string? ipAddress, string? userAgent, CancellationToken ct = default);

    /// <summary>Lịch sử thay đổi tài khoản ngân hàng của chính user này, mới nhất trước.</summary>
    Task<List<BankAccountAuditLogResponse>> GetHistoryAsync(string userId, CancellationToken ct = default);
}
