namespace MV.DomainLayer.Constants;

/// <summary>
/// Hướng sắp xếp theo thời gian tạo cho các danh sách admin.
/// Dùng chung để tham số của mọi endpoint danh sách nhận cùng một bộ giá trị.
/// </summary>
public static class ListSortDirection
{
    /// <summary>Cũ nhất trước.</summary>
    public const string Ascending = "asc";

    /// <summary>Mới nhất trước — mặc định của mọi danh sách admin.</summary>
    public const string Descending = "desc";

    /// <summary>
    /// True khi client yêu cầu cũ-nhất-trước. Giá trị lạ hoặc rỗng đều rơi về
    /// mới-nhất-trước, để một tham số sai chính tả không âm thầm đảo thứ tự bảng.
    /// </summary>
    public static bool IsAscending(string? sortDirection) =>
        string.Equals(sortDirection, Ascending, StringComparison.OrdinalIgnoreCase);
}
