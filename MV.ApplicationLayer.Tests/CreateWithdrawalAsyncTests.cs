using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Exceptions;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "CreateWithdrawalAsync" (Code_32, WalletService.CreateWithdrawalAsync).
// Only the bank-info validation runs before the wallet row lock
// (FromSqlRaw(SqlQueries.LockWalletByUserId, ...)), so it's the only branch testable on EF
// InMemory - wallet not found, insufficient balance, pending withdrawal exists, amount below
// minimum, and the success path all need a real Postgres connection and were verified separately.
public class CreateWithdrawalAsyncTests
{
    [Fact]
    public async Task MissingBankInfo_ThrowsBankInfoRequiredException()
    {
        var service = new WalletService(
            TestSupport.CreateInMemoryContext("create-withdrawal"),
            null!,
            new FakeNotificationService(),
            new FakeFileStorageService(),
            NullLogger<WalletService>.Instance);
        var request = new CreateWithdrawalRequest { Amount = 50000, BankName = null, AccountNumber = null, AccountHolderName = null };

        await Assert.ThrowsAsync<BankInfoRequiredException>(() => service.CreateWithdrawalAsync("user-1", request));
    }
}
