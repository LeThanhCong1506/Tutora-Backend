using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class TutorResponseTimeoutPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 14, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CanProcess_ReturnsTrue_AtExactDeadline()
    {
        var booking = PendingBooking(responseDeadline: Now);

        Assert.True(TutorResponseTimeoutPolicy.CanProcess(booking, Now));
    }

    [Fact]
    public void CanProcess_ReturnsFalse_BeforeDeadline()
    {
        var booking = PendingBooking(responseDeadline: Now.AddTicks(1));

        Assert.False(TutorResponseTimeoutPolicy.CanProcess(booking, Now));
    }

    [Theory]
    [InlineData(BookingStatus.DepositPaid, null)]
    [InlineData(BookingStatus.Cancelled, null)]
    [InlineData(BookingStatus.PendingTutor, RefundStatus.Refunded)]
    public void CanProcess_ReturnsFalse_ForTerminalOrAlreadyRefundedBooking(string status, string? refundStatus)
    {
        var booking = PendingBooking(responseDeadline: Now);
        booking.Status = status;
        booking.Refundstatus = refundStatus;

        Assert.False(TutorResponseTimeoutPolicy.CanProcess(booking, Now));
    }

    [Fact]
    public void RefundAmounts_UseDepositAndOneSessionEscrow_ForDepositPhase()
    {
        var booking = PendingBooking(responseDeadline: Now);
        booking.Paymentstatus = PaymentStatus.DepositEscrowed;
        booking.Depositamount = 120_000m;
        booking.Tutorfee = 400_000m;
        booking.Totalsessions = 4;

        Assert.Equal(120_000m, TutorResponseTimeoutPolicy.ParentRefundAmount(booking));
        Assert.Equal(100_000m, TutorResponseTimeoutPolicy.TutorEscrowAmount(booking));
    }

    [Fact]
    public void RefundAmounts_UseFullPaidAmounts_ForSingleSessionOrFullyEscrowedBooking()
    {
        var booking = PendingBooking(responseDeadline: Now);
        booking.Paymentstatus = PaymentStatus.Escrowed;
        booking.Finalprice = 210_000m;
        booking.Tutorfee = 190_000m;
        booking.Totalsessions = 1;
        booking.Remainingpaidat = Now;

        Assert.Equal(210_000m, TutorResponseTimeoutPolicy.ParentRefundAmount(booking));
        Assert.Equal(190_000m, TutorResponseTimeoutPolicy.TutorEscrowAmount(booking));
    }

    private static Booking PendingBooking(DateTime responseDeadline)
        => new()
        {
            Status = BookingStatus.PendingTutor,
            Responsedeadline = responseDeadline,
            Depositpaidat = Now.AddHours(-24),
            Paymentstatus = PaymentStatus.DepositEscrowed
        };
}
