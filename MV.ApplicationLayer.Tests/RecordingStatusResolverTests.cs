using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class RecordingStatusResolverTests
{
    [Fact]
    public void Resolve_RoomOpenWithSid_IsRecording()
    {
        var (status, url) = RecordingStatusResolver.Resolve(url: null, s3key: null, sid: "sid-1", roomClosed: false);

        Assert.Equal("recording", status);
        Assert.Null(url);
    }

    [Fact]
    public void Resolve_RoomOpenWithoutSid_IsNone()
    {
        var (status, _) = RecordingStatusResolver.Resolve(url: null, s3key: null, sid: null, roomClosed: false);

        Assert.Equal("none", status);
    }

    [Fact]
    public void Resolve_RoomOpenIgnoresLeftoverUrl()
    {
        // Url/s3key chỉ được gán lúc check-out — còn giá trị khi phòng chưa đóng nghĩa là rác của lượt ghi trước.
        var (status, url) = RecordingStatusResolver.Resolve("https://drive/old", "old.mp4", "sid-1", roomClosed: false);

        Assert.Equal("recording", status);
        Assert.Null(url);
    }

    [Fact]
    public void Resolve_RoomClosedWithUrl_IsAvailable()
    {
        var (status, url) = RecordingStatusResolver.Resolve("https://drive/file", null, "sid-1", roomClosed: true);

        Assert.Equal("available", status);
        Assert.Equal("https://drive/file", url);
    }

    [Fact]
    public void Resolve_RoomClosedWithS3KeyOnly_IsProcessing()
    {
        var (status, _) = RecordingStatusResolver.Resolve(null, "recordings/515/x.mp4", "sid-1", roomClosed: true);

        Assert.Equal("processing", status);
    }

    [Fact]
    public void Resolve_RoomClosedWithSidButNoFile_IsFailedNotRecording()
    {
        // Trường hợp buổi 515/516 trên dev: stop không ra file nào, sid còn lại.
        // Phòng đã đóng thì không thể "đang ghi" — phải báo hỏng.
        var (status, url) = RecordingStatusResolver.Resolve(null, null, "sid-1", roomClosed: true);

        Assert.Equal("failed", status);
        Assert.Null(url);
    }

    [Fact]
    public void Resolve_RoomClosedWithNothing_IsNone()
    {
        var (status, _) = RecordingStatusResolver.Resolve(null, null, null, roomClosed: true);

        Assert.Equal("none", status);
    }
}
