using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IDisputeRepository
{
    // Queries
    IQueryable<Dispute> GetBaseQuery();
    Task<Dispute?> GetDetailAsync(int disputeId);           // includes ClassSession.Tutor.Tutor, CreatedBy, ResolvedBy, Booking.Subject
    Task<Dispute?> FindWithBookingAsync(int disputeId);     // includes Booking
    Task<Dispute?> FindWithClassSessionAsync(int disputeId);      // includes ClassSession

    // Stats
    Task<int> CountByStatusAsync(string status);
    Task<int> CountResolvedSinceAsync(DateTime since);
    Task<decimal> SumRefundedSinceAsync(DateTime since);

    // Warnings count
    Task<int> CountWarningsByTutorAsync(string tutorId);

    // Chat for dispute
    Task<int?> GetChannelIdForBookingAsync(int bookingId);
    Task<List<Chatmessage>> GetChannelMessagesAsync(int channelId, int limit = 100);

    // Mutations
    void Add(Dispute dispute);

    Task<int> SaveChangesAsync();
}
