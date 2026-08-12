using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Văn bản pháp lý công khai (điều khoản, bảo mật, cookie, quy tắc cộng đồng, thoả thuận gia sư).
/// Nội dung là Markdown để admin sửa được qua CMS mà không cần đụng code hay deploy lại.
/// </summary>
public partial class PolicyDocument
{
    public int Policydocumentid { get; set; }

    /// <summary>Định danh trên URL công khai (/terms, /privacy...). Duy nhất, không đổi sau khi phát hành.</summary>
    public string Slug { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Summary { get; set; }

    public string Contentmarkdown { get; set; } = null!;

    public string Version { get; set; } = null!;

    public DateOnly? Effectivedate { get; set; }

    /// <summary>draft | published | archived. `archived` là xoá mềm — không xoá cứng văn bản người dùng đã đồng ý.</summary>
    public string Status { get; set; } = null!;

    public int Displayorder { get; set; }

    public DateTime? Publishedat { get; set; }

    public DateTime Createdat { get; set; }

    public DateTime Updatedat { get; set; }

    public string? Updatedby { get; set; }

    public virtual User? UpdatedbyNavigation { get; set; }
}
