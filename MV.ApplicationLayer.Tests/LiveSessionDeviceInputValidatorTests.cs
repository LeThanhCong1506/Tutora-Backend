using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class LiveSessionDeviceInputValidatorTests
{
    [Fact]
    public void Guid_WithHyphens_IsNormalizedToCompactLowercaseValue()
    {
        var id = Guid.NewGuid();

        var valid = LiveSessionDeviceInputValidator.TryNormalizeGuid(id.ToString("D").ToUpperInvariant(), out var normalized);

        Assert.True(valid);
        Assert.Equal(id.ToString("N"), normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MissingOrInvalidGuid_IsRejected(string? value)
    {
        Assert.False(LiveSessionDeviceInputValidator.TryNormalizeGuid(value, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void DeviceLabel_IsTrimmedAndAcceptsMaximumLength()
    {
        var label = new string('a', LiveSessionDeviceInputValidator.MaxDeviceLabelLength);

        Assert.True(LiveSessionDeviceInputValidator.TryNormalizeDeviceLabel($"  {label}  ", out var normalized));
        Assert.Equal(label, normalized);
    }

    [Fact]
    public void DeviceLabel_OverMaximumLength_IsRejected()
    {
        var label = new string('a', LiveSessionDeviceInputValidator.MaxDeviceLabelLength + 1);

        Assert.False(LiveSessionDeviceInputValidator.TryNormalizeDeviceLabel(label, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Chrome\nWindows")]
    [InlineData("Phone\u0000Android")]
    public void MissingOrControlCharacterDeviceLabel_IsRejected(string? value)
    {
        Assert.False(LiveSessionDeviceInputValidator.TryNormalizeDeviceLabel(value, out _));
    }
}
