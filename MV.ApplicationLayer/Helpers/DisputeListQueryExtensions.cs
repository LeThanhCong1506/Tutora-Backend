using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

public static class DisputeListQueryExtensions
{
    public static IQueryable<Dispute> ApplyPortalFilters(
        this IQueryable<Dispute> disputes,
        PortalDisputeQueryRequest query)
    {
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            disputes = disputes.Where(dispute => dispute.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.DisputeType))
        {
            var disputeType = query.DisputeType.Trim();
            disputes = disputes.Where(dispute => dispute.Disputetype == disputeType);
        }

        return disputes.ApplyDisputeSearch(query.Search, includeParticipantNames: true);
    }

    public static IQueryable<Dispute> ApplyDisputeSearch(
        this IQueryable<Dispute> disputes,
        string? search,
        bool includeParticipantNames = false)
    {
        var normalized = search?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return disputes;

        if (DisputeSearchParser.TryParseIdentifier(normalized, out var identifier))
        {
            return identifier.Kind switch
            {
                DisputeIdentifierKind.Booking => disputes.Where(dispute => dispute.Bookingid == identifier.Id),
                DisputeIdentifierKind.ClassSession => disputes.Where(dispute => dispute.Classsessionid == identifier.Id),
                DisputeIdentifierKind.Dispute => disputes.Where(dispute => dispute.Disputeid == identifier.Id),
                _ => disputes.Where(dispute =>
                    dispute.Disputeid == identifier.Id
                    || dispute.Bookingid == identifier.Id
                    || dispute.Classsessionid == identifier.Id)
            };
        }

        var searchText = normalized.ToLower();
        if (includeParticipantNames)
        {
            return disputes.Where(dispute =>
                (dispute.Reason != null && dispute.Reason.ToLower().Contains(searchText))
                || (dispute.CreatedbyNavigation != null
                    && dispute.CreatedbyNavigation.Fullname != null
                    && dispute.CreatedbyNavigation.Fullname.ToLower().Contains(searchText))
                || (dispute.ClassSession != null
                    && dispute.ClassSession.Tutor != null
                    && dispute.ClassSession.Tutor.Tutor != null
                    && dispute.ClassSession.Tutor.Tutor.Fullname != null
                    && dispute.ClassSession.Tutor.Tutor.Fullname.ToLower().Contains(searchText)));
        }

        return disputes.Where(dispute =>
            dispute.Reason != null && dispute.Reason.ToLower().Contains(searchText));
    }

    public static IOrderedQueryable<Dispute> OrderForDisputeList(
        this IQueryable<Dispute> disputes,
        string? sortDirection)
    {
        return ListSortDirection.IsAscending(sortDirection)
            ? disputes.OrderBy(dispute => dispute.Createdat).ThenBy(dispute => dispute.Disputeid)
            : disputes.OrderByDescending(dispute => dispute.Createdat).ThenByDescending(dispute => dispute.Disputeid);
    }
}
