using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class BookingScheduleLockPolicyTests
{
    [Theory]
    [InlineData(BookingStatus.Accepted, true)]
    [InlineData(BookingStatus.DepositPaid, true)]
    [InlineData(BookingStatus.PendingRemainingPayment, true)]
    [InlineData(BookingStatus.Paid, true)]
    [InlineData(BookingStatus.Ongoing, true)]
    [InlineData(BookingStatus.Completed, true)]
    [InlineData(BookingStatus.PendingTutor, false)]
    [InlineData(BookingStatus.PendingPayment, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.CancelledNoshow, false)]
    [InlineData(BookingStatus.PaymentTimeout, false)]
    [InlineData(null, false)]
    public void IsLockingStatus_MatchesExpected(string? status, bool expected)
    {
        Assert.Equal(expected, BookingScheduleLockPolicy.IsLockingStatus(status));
    }

    [Theory]
    [InlineData(BookingStatus.PendingTutor, true)]
    [InlineData(BookingStatus.PendingPayment, true)]
    [InlineData(BookingStatus.DepositPaid, false)]
    [InlineData(BookingStatus.Accepted, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(null, false)]
    public void IsCompetingStatus_MatchesExpected(string? status, bool expected)
    {
        Assert.Equal(expected, BookingScheduleLockPolicy.IsCompetingStatus(status));
    }

    [Fact]
    public void LockingAndCompeting_AreMutuallyExclusive_ForEveryKnownStatus()
    {
        var allStatuses = new[]
        {
            BookingStatus.PendingTutor, BookingStatus.Accepted, BookingStatus.PendingPayment,
            BookingStatus.Paid, BookingStatus.PaymentTimeout, BookingStatus.Ongoing,
            BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.CancelledNoshow,
            BookingStatus.CancelledByStaff, BookingStatus.CancelledByDispute, BookingStatus.DepositPaid,
            BookingStatus.PendingRemainingPayment,
        };

        foreach (var status in allStatuses)
        {
            var locking = BookingScheduleLockPolicy.IsLockingStatus(status);
            var competing = BookingScheduleLockPolicy.IsCompetingStatus(status);
            Assert.False(locking && competing, $"Status '{status}' can't be both locking and competing.");
        }
    }
}
