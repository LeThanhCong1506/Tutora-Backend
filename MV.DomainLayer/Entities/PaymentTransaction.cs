using System;

namespace MV.DomainLayer.Entities;

public partial class PaymentTransaction
{
    public int Paymenttransactionid { get; set; }

    public string? Userid { get; set; }

    public string Paymentmethod { get; set; } = null!;

    public string Direction { get; set; } = null!;

    public string Purpose { get; set; } = null!;

    public string Status { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = null!;

    public long? Ordercode { get; set; }

    public string? Providertransactionid { get; set; }

    public string? Paymentlinkid { get; set; }

    public int? Paymentrequestid { get; set; }

    public int? Bookingid { get; set; }

    public int? Withdrawalid { get; set; }

    /// <summary>Gói AI credit được mua; null cho giao dịch khác.</summary>
    public int? AiCreditPackageid { get; set; }

    /// <summary>Tài khoản được cộng credit khi mua gói; null cho giao dịch khác.
    public string? AiCreditUserid { get; set; }

    public string? Description { get; set; }

    public DateTime? Paidat { get; set; }

    public DateTime? Createdat { get; set; }

    public string? Processedby { get; set; }

    public string? Note { get; set; }

    public string Capturesource { get; set; } = null!;

    public string Reconciliationstatus { get; set; } = null!;

    public string? Capturefingerprint { get; set; }

    public string? Webhookcode { get; set; }

    public string? Webhookdesc { get; set; }

    public bool? Webhooksuccess { get; set; }

    public string? Providercode { get; set; }

    public string? Providerdesc { get; set; }

    public string? Sourceaccountbankid { get; set; }

    public string? Sourceaccountbankname { get; set; }

    public string? Sourceaccountnumber { get; set; }

    public string? Sourceaccountname { get; set; }

    public string? Destinationaccountbankbin { get; set; }

    public string? Destinationaccountbankname { get; set; }

    public string? Destinationaccountnumber { get; set; }

    public string? Destinationaccountname { get; set; }

    public string? Destinationvirtualaccountnumber { get; set; }

    public string? Destinationvirtualaccountname { get; set; }

    public string? Providerpayload { get; set; }

    public string? Webhookpayload { get; set; }

    public string? Proofimagepath { get; set; }

    public virtual User? User { get; set; }

    public virtual User? ProcessedbyNavigation { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual PaymentRequest? Paymentrequest { get; set; }

    public virtual Withdrawalrequest? Withdrawal { get; set; }
}
