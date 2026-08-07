using MV.DomainLayer.DTO;
using MV.DomainLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Cột `ClassSessionReport.Attachments` đã đi qua 3 định dạng; báo cáo cũ trong DB không được migrate
/// nên serializer phải đọc được cả 3, nếu không phụ huynh sẽ mất tài liệu của những buổi học đã nộp.
/// </summary>
public class ReportAttachmentSerializerTests
{
    [Fact]
    public void Deserialize_ReadsLegacyCommaSeparatedFormat()
    {
        var result = ReportAttachmentSerializer.Deserialize("https://cdn/a.jpg,https://cdn/b.pdf");

        Assert.NotNull(result);
        Assert.Equal(new[] { "https://cdn/a.jpg", "https://cdn/b.pdf" }, result!.Select(x => x.Url));
        Assert.All(result, item => Assert.Null(item.Description));
    }

    [Fact]
    public void Deserialize_ReadsPlainUrlArrayFormat()
    {
        var result = ReportAttachmentSerializer.Deserialize("[\"https://cdn/a.jpg\",\"https://cdn/b.pdf\"]");

        Assert.NotNull(result);
        Assert.Equal(new[] { "https://cdn/a.jpg", "https://cdn/b.pdf" }, result!.Select(x => x.Url));
        Assert.All(result, item => Assert.Null(item.Description));
    }

    [Fact]
    public void Deserialize_ReadsObjectArrayWithDescriptions()
    {
        var result = ReportAttachmentSerializer.Deserialize(
            "[{\"url\":\"https://cdn/a.jpg\",\"description\":\"Đề bài buổi 5\"},{\"url\":\"https://cdn/b.pdf\"}]");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("https://cdn/a.jpg", result[0].Url);
        Assert.Equal("Đề bài buổi 5", result[0].Description);
        Assert.Equal("https://cdn/b.pdf", result[1].Url);
        Assert.Null(result[1].Description);
    }

    [Fact]
    public void SerializeThenDeserialize_KeepsDescriptions()
    {
        var stored = ReportAttachmentSerializer.Serialize(new[]
        {
            new ReportAttachment { Url = " https://cdn/a.jpg ", Description = "  Đề bài buổi 5  " },
            new ReportAttachment { Url = "https://cdn/b.pdf", Description = "   " },
        });

        var result = ReportAttachmentSerializer.Deserialize(stored);

        Assert.NotNull(result);
        Assert.Equal("https://cdn/a.jpg", result![0].Url);
        Assert.Equal("Đề bài buổi 5", result[0].Description);
        // Mô tả toàn khoảng trắng coi như không có, để FE fallback về tên file.
        Assert.Null(result[1].Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Deserialize_ReturnsNullForEmptyColumn(string? stored)
    {
        Assert.Null(ReportAttachmentSerializer.Deserialize(stored));
    }

    [Fact]
    public void Serialize_ReturnsNullWhenNothingToStore()
    {
        Assert.Null(ReportAttachmentSerializer.Serialize(null));
        Assert.Null(ReportAttachmentSerializer.Serialize(Array.Empty<ReportAttachment>()));
        Assert.Null(ReportAttachmentSerializer.Serialize(new[] { new ReportAttachment { Url = "  " } }));
    }
}
