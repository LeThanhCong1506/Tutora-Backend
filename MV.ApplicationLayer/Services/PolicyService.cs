using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel.Policy;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel.Policy;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

public class PolicyService : IPolicyService
{
    private const string StatusDraft = "draft";
    private const string StatusPublished = "published";
    private const string StatusArchived = "archived";

    private readonly IAppDbContext _context;

    public PolicyService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<PolicyDocumentSummaryResponse>> GetPublishedAsync(CancellationToken ct = default)
    {
        var documents = await _context.PolicyDocuments
            .AsNoTracking()
            .Where(d => d.Status == StatusPublished)
            .OrderBy(d => d.Displayorder)
            .ThenBy(d => d.Policydocumentid)
            .ToListAsync(ct);
        return documents.Select(d => ToSummary(d, updatedByName: null)).ToList();
    }

    public async Task<PolicyDocumentResponse?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default)
    {
        var normalized = (slug ?? string.Empty).Trim().ToLowerInvariant();

        var document = await _context.PolicyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Slug == normalized && d.Status == StatusPublished, ct);

        return document is null ? null : ToDetail(document, updatedByName: null);
    }

    public async Task<List<PolicyDocumentSummaryResponse>> GetAllAsync(bool includeArchived, CancellationToken ct = default)
    {
        var query = _context.PolicyDocuments.AsNoTracking();
        if (!includeArchived) query = query.Where(d => d.Status != StatusArchived);

        var rows = await query
            .OrderBy(d => d.Displayorder)
            .ThenBy(d => d.Policydocumentid)
            .Select(d => new { Document = d, UpdatedByName = d.UpdatedbyNavigation!.Fullname })
            .ToListAsync(ct);

        return rows.Select(r => ToSummary(r.Document, r.UpdatedByName)).ToList();
    }

    public async Task<PolicyDocumentResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var row = await _context.PolicyDocuments
            .AsNoTracking()
            .Where(d => d.Policydocumentid == id)
            .Select(d => new { Document = d, UpdatedByName = d.UpdatedbyNavigation!.Fullname })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : ToDetail(row.Document, row.UpdatedByName);
    }

    public async Task<PolicyDocumentResponse> CreateAsync(
        CreatePolicyDocumentRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await _context.PolicyDocuments.AnyAsync(d => d.Slug == slug, ct))
            throw new ArgumentException($"Slug \"{slug}\" đã được dùng cho một văn bản khác.");

        // Không truyền thứ tự thì xếp xuống cuối danh sách. Để mặc định 0 thì mọi văn bản mới
        // dồn hết lên đầu sidebar trang công khai, chen trước cả bản giới thiệu.
        var displayOrder = request.DisplayOrder
            ?? await _context.PolicyDocuments.MaxAsync(d => (int?)d.Displayorder, ct) + 1
            ?? 0;

        var now = DateTime.UtcNow;
        var document = new PolicyDocument
        {
            Slug = slug,
            Title = request.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
            Contentmarkdown = request.ContentMarkdown,
            Version = string.IsNullOrWhiteSpace(request.Version) ? "1.0" : request.Version.Trim(),
            Effectivedate = request.EffectiveDate,
            // Luôn tạo ở dạng nháp — văn bản pháp lý không được lộ ra trang công khai lúc còn soạn dở.
            Status = StatusDraft,
            Displayorder = displayOrder,
            Createdat = now,
            Updatedat = now,
            Updatedby = actorUserId,
        };

        _context.PolicyDocuments.Add(document);
        await _context.SaveChangesAsync(ct);

        return ToDetail(document, updatedByName: null);
    }

    public async Task<PolicyDocumentResponse> UpdateAsync(
        int id, UpdatePolicyDocumentRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var document = await _context.PolicyDocuments.FirstOrDefaultAsync(d => d.Policydocumentid == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy văn bản chính sách.");

        document.Title = request.Title.Trim();
        document.Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        document.Contentmarkdown = request.ContentMarkdown;
        if (!string.IsNullOrWhiteSpace(request.Version)) document.Version = request.Version.Trim();
        document.Effectivedate = request.EffectiveDate;
        if (request.DisplayOrder.HasValue) document.Displayorder = request.DisplayOrder.Value;
        document.Updatedat = DateTime.UtcNow;
        document.Updatedby = actorUserId;

        await _context.SaveChangesAsync(ct);
        return ToDetail(document, updatedByName: null);
    }

    public async Task<PolicyDocumentResponse> SetPublishedAsync(
        int id, bool publish, string? actorUserId, CancellationToken ct = default)
    {
        var document = await _context.PolicyDocuments.FirstOrDefaultAsync(d => d.Policydocumentid == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy văn bản chính sách.");

        document.Status = publish ? StatusPublished : StatusDraft;
        // Giữ mốc xuất bản đầu tiên; gỡ xuống nháp rồi đăng lại không reset lịch sử.
        if (publish) document.Publishedat ??= DateTime.UtcNow;
        document.Updatedat = DateTime.UtcNow;
        document.Updatedby = actorUserId;

        await _context.SaveChangesAsync(ct);
        return ToDetail(document, updatedByName: null);
    }

    public async Task ReorderAsync(ReorderRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var orders = request.Items
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().DisplayOrder);

        var documents = await _context.PolicyDocuments
            .Where(d => orders.Keys.Contains(d.Policydocumentid))
            .ToListAsync(ct);

        if (documents.Count != orders.Count)
            throw new KeyNotFoundException("Danh sách sắp xếp có văn bản không tồn tại.");

        var now = DateTime.UtcNow;
        foreach (var document in documents)
        {
            var order = orders[document.Policydocumentid];
            if (document.Displayorder == order) continue;

            document.Displayorder = order;
            document.Updatedat = now;
            document.Updatedby = actorUserId;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(int id, string? actorUserId, CancellationToken ct = default)
    {
        var document = await _context.PolicyDocuments.FirstOrDefaultAsync(d => d.Policydocumentid == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy văn bản chính sách.");

        document.Status = StatusArchived;
        document.Updatedat = DateTime.UtcNow;
        document.Updatedby = actorUserId;

        await _context.SaveChangesAsync(ct);
    }

    public async Task<PolicyDocumentResponse> RestoreAsync(int id, string? actorUserId, CancellationToken ct = default)
    {
        var document = await _context.PolicyDocuments.FirstOrDefaultAsync(d => d.Policydocumentid == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy văn bản chính sách.");

        if (document.Status != StatusArchived)
            throw new InvalidOperationException("Văn bản này không nằm trong kho lưu trữ.");

        // Về nháp chứ không về `published`: khôi phục xong phải đọc lại rồi mới cho lên trang công khai.
        document.Status = StatusDraft;
        document.Updatedat = DateTime.UtcNow;
        document.Updatedby = actorUserId;

        await _context.SaveChangesAsync(ct);
        return ToDetail(document, updatedByName: null);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var document = await _context.PolicyDocuments.FirstOrDefaultAsync(d => d.Policydocumentid == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy văn bản chính sách.");

        // Chặn xoá thẳng một văn bản đang phát hành hoặc đang soạn: phải lưu trữ trước đã.
        if (document.Status != StatusArchived)
            throw new InvalidOperationException("Chỉ xoá vĩnh viễn được văn bản đã nằm trong kho lưu trữ.");

        _context.PolicyDocuments.Remove(document);
        await _context.SaveChangesAsync(ct);
    }

    // ── Mapping ──

    private static PolicyDocumentSummaryResponse ToSummary(PolicyDocument d, string? updatedByName) => new()
    {
        PolicyDocumentId = d.Policydocumentid,
        Slug = d.Slug,
        Title = d.Title,
        Summary = d.Summary,
        Version = d.Version,
        EffectiveDate = d.Effectivedate,
        Status = d.Status,
        DisplayOrder = d.Displayorder,
        PublishedAt = d.Publishedat,
        UpdatedAt = d.Updatedat,
        UpdatedByName = updatedByName,
    };

    private static PolicyDocumentResponse ToDetail(PolicyDocument d, string? updatedByName) => new()
    {
        PolicyDocumentId = d.Policydocumentid,
        Slug = d.Slug,
        Title = d.Title,
        Summary = d.Summary,
        Version = d.Version,
        EffectiveDate = d.Effectivedate,
        Status = d.Status,
        DisplayOrder = d.Displayorder,
        PublishedAt = d.Publishedat,
        UpdatedAt = d.Updatedat,
        UpdatedByName = updatedByName,
        ContentMarkdown = d.Contentmarkdown,
    };
}
