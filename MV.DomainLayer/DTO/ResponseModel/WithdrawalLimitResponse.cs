namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Ngưỡng rút tiền tối thiểu của nền tảng, cho FE (gia sư/phụ huynh/học sinh) đọc để validate
/// ngay trên form thay vì hardcode — giá trị do admin cấu hình trong system_configs.
/// </summary>
public class WithdrawalLimitResponse
{
    public decimal MinWithdrawalAmount { get; set; }
}
