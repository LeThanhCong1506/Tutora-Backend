using System;
using System.Collections.Generic;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>Gói AI credit hiển thị cho client / quản lý cho admin.</summary>
public class AiCreditPackageResponse
{
    public int PackageId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int CreditAmount { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = null!;
    public bool IsPurchasable { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
}

/// <summary>Số dư AI credit của tài khoản đang đăng nhập.</summary>
public class AiCreditBalanceResponse
{
    public int Balance { get; set; }
}

/// <summary>Một dòng lịch sử ledger AI credit.</summary>
public class AiCreditTransactionResponse
{
    public int TransactionId { get; set; }
    public int Amount { get; set; }
    public int BalanceAfter { get; set; }
    public string Source { get; set; } = null!;
    public string? ReferenceId { get; set; }
    public string? Description { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>Kết quả khởi tạo mua gói — trả link + QR PayOS cho client hiển thị.</summary>
public class AiCreditPurchaseResponse
{
    public long OrderCode { get; set; }
    public string CheckoutUrl { get; set; } = null!;
    /// <summary>Chuỗi QR thô (VietQR) để FE render ảnh QR trong modal.</summary>
    public string? QrCode { get; set; }
    public string? PaymentLinkId { get; set; }
    public decimal Amount { get; set; }
    public int PackageId { get; set; }
}
