using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class DisputeSearchParserTests
{
    [Theory]
    [InlineData("11", DisputeIdentifierKind.Any, 11)]
    [InlineData("#11", DisputeIdentifierKind.Any, 11)]
    [InlineData("Booking #159", DisputeIdentifierKind.Booking, 159)]
    [InlineData("buổi học #161", DisputeIdentifierKind.ClassSession, 161)]
    [InlineData("session 42", DisputeIdentifierKind.ClassSession, 42)]
    [InlineData("Khiếu nại #9", DisputeIdentifierKind.Dispute, 9)]
    [InlineData("dispute 17", DisputeIdentifierKind.Dispute, 17)]
    public void TryParseIdentifier_RecognizesSupportedIdentifierForms(
        string query,
        DisputeIdentifierKind expectedKind,
        int expectedId)
    {
        var parsed = DisputeSearchParser.TryParseIdentifier(query, out var identifier);

        Assert.True(parsed);
        Assert.Equal(expectedKind, identifier.Kind);
        Assert.Equal(expectedId, identifier.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("đợi 30 phút")]
    [InlineData("test 159")]
    [InlineData("Booking")]
    [InlineData("#")]
    public void TryParseIdentifier_LeavesNaturalLanguageAndIncompleteIdsAsText(string? query)
    {
        Assert.False(DisputeSearchParser.TryParseIdentifier(query, out _));
    }
}
