using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using static MV.DomainLayer.Constants.ClassSessionStatus;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class DisputeSettlementPolicyTests
{
    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.CancelledNoshow)]
    public void IsTerminalBooking_BlocksFinishedBookings(string status)
    {
        Assert.True(DisputeSettlementPolicy.IsTerminalBooking(status));
    }

    [Theory]
    [InlineData(BookingStatus.PendingTutor)]
    [InlineData(BookingStatus.Accepted)]
    [InlineData(BookingStatus.DepositPaid)]
    [InlineData(BookingStatus.PendingRemainingPayment)]
    [InlineData(BookingStatus.Ongoing)]
    public void IsTerminalBooking_AllowsActiveBookings(string status)
    {
        Assert.False(DisputeSettlementPolicy.IsTerminalBooking(status));
    }

    [Theory]
    [InlineData(PendingConfirmation)]
    [InlineData(Completed)]
    public void IsEligibleClassSession_AllowsOccurredSessions(string status)
    {
        Assert.True(DisputeSettlementPolicy.IsEligibleClassSession(status));
    }

    [Theory]
    [InlineData(Scheduled)]
    [InlineData(InProgress)]
    [InlineData(Disputed)]
    [InlineData(NoShow)]
    [InlineData(Cancelled)]
    public void IsEligibleClassSession_RejectsOtherSessionFlows(string status)
    {
        Assert.False(DisputeSettlementPolicy.IsEligibleClassSession(status));
    }

    [Fact]
    public void SettledSession_ReopensExactlyOneCounterUnit()
    {
        const int beforeOpeningDispute = 3;

        var afterOpeningDispute = DisputeSettlementPolicy.SessionsRemainingAfterOpeningDispute(
            beforeOpeningDispute,
            wasSettled: true);
        var afterAdminResolution = afterOpeningDispute - 1;

        Assert.Equal(4, afterOpeningDispute);
        Assert.Equal(beforeOpeningDispute, afterAdminResolution);
    }

    [Fact]
    public void UnsettledSession_DoesNotChangeCounterWhenDisputeOpens()
    {
        const int beforeOpeningDispute = 3;

        var afterOpeningDispute = DisputeSettlementPolicy.SessionsRemainingAfterOpeningDispute(
            beforeOpeningDispute,
            wasSettled: false);

        Assert.Equal(beforeOpeningDispute, afterOpeningDispute);
    }
}
