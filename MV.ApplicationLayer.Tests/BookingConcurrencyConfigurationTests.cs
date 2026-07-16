using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class BookingConcurrencyConfigurationTests
{
    [Fact]
    public void BookingStatus_IsConfiguredAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model_only;Username=model_only;Password=model_only",
                npgsql => npgsql.UseVector())
            .Options;

        using var context = new AgoraDbContext(options);
        var statusProperty = context.Model
            .FindEntityType(typeof(Booking))!
            .FindProperty(nameof(Booking.Status));

        Assert.NotNull(statusProperty);
        Assert.True(statusProperty!.IsConcurrencyToken);
    }

    [Fact]
    public void BookingLockQuery_RemainsComposableForSingleOrDefaultExecution()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model_only;Username=model_only;Password=model_only",
                npgsql => npgsql.UseVector())
            .Options;

        using var context = new AgoraDbContext(options);
        var sql = context.Bookings
            .FromSqlRaw(SqlQueries.LockBookingById, 42)
            .Take(2)
            .ToQueryString();

        Assert.Contains("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
