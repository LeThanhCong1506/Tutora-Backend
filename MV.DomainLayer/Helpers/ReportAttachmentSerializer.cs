using System.Text.Json;
using MV.DomainLayer.DTO;

namespace MV.DomainLayer.Helpers;

/// <summary>
/// Đọc/ghi cột `ClassSessionReport.Attachments`. Cột này đã đi qua 3 định dạng nên mọi nơi đọc
/// phải dùng chung helper này, tránh mỗi service tự parse một kiểu:
///   1. CSV cũ:            "url1,url2"
///   2. Mảng URL:          ["url1","url2"]
///   3. Mảng object (mới): [{"url":"...","description":"Đề bài buổi 5"}]
/// Ghi thì luôn ghi định dạng (3); dữ liệu cũ vẫn đọc được nên không cần migrate.
/// </summary>
public static class ReportAttachmentSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IEnumerable<ReportAttachment>? attachments)
    {
        var items = attachments?
            .Where(a => !string.IsNullOrWhiteSpace(a.Url))
            .Select(a => new ReportAttachment
            {
                Url = a.Url.Trim(),
                Description = string.IsNullOrWhiteSpace(a.Description) ? null : a.Description.Trim(),
            })
            .ToList();

        return items is { Count: > 0 } ? JsonSerializer.Serialize(items, Options) : null;
    }

    public static List<ReportAttachment>? Deserialize(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        if (stored.TrimStart().StartsWith('['))
        {
            // Định dạng (3)
            try
            {
                var items = JsonSerializer.Deserialize<List<ReportAttachment>>(stored, Options);
                if (items is { Count: > 0 } && items.All(item => !string.IsNullOrWhiteSpace(item.Url)))
                    return items;
            }
            catch (JsonException)
            {
                // Không phải mảng object — thử tiếp định dạng (2).
            }

            // Định dạng (2)
            try
            {
                var urls = JsonSerializer.Deserialize<List<string>>(stored, Options);
                if (urls is not null) return ToAttachments(urls);
            }
            catch (JsonException)
            {
                // Chuỗi hỏng — rơi xuống nhánh CSV bên dưới.
            }
        }

        // Định dạng (1)
        return ToAttachments(stored.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    public static List<string>? ToUrls(IEnumerable<ReportAttachment>? attachments) =>
        attachments?.Select(a => a.Url).ToList();

    private static List<ReportAttachment> ToAttachments(IEnumerable<string> urls) =>
        urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => new ReportAttachment { Url = url.Trim() })
            .ToList();
}
