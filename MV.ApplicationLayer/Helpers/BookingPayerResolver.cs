using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Resolves the actual counterparty on a booking's "parent side" — the parent for a
/// parent-managed student, or the student's own login for a self-managed student
/// (Parentid is always null for self-managed bookings). Booking.Studentid is safe as the
/// final fallback: every self-managed Studentprofile is created with Studentid set to its
/// own Linkeduserid/user id (see SimpleAuthService, SocialRegistrationService, UserService.Admin).
/// </summary>
public static class BookingPayerResolver
{
    public static (string? Id, bool IsStudent) Resolve(Booking booking)
    {
        if (!string.IsNullOrWhiteSpace(booking.Parentid))
            return (booking.Parentid, false);
        if (!string.IsNullOrWhiteSpace(booking.Student?.Linkeduserid))
            return (booking.Student!.Linkeduserid, true);
        return (booking.Studentid, true);
    }
}
