using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Admin;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Covers Excel UTCIDs whose rule lives on the request DTO as a DataAnnotation rather than inside
// the service. ASP.NET model binding runs these before the action body, so the service never sees
// an invalid value - which is why the service-level tests can't reach these branches. Validating
// the annotations directly tests the rule where it actually is.
//   CreateFeedbackAsync            - Rating must be 1..5
//   SendChatMessageAsync           - Content required, max 2000 chars
//   ApproveWithdrawalRequestAsync  - Note min 3 chars
public class RequestValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void CreateFeedbackRequest_RatingOutsideOneToFive_FailsValidation(int rating)
    {
        var request = new CreateFeedbackRequest { ClassSessionId = 1, Rating = rating };

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateFeedbackRequest.Rating)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void CreateFeedbackRequest_RatingOnBoundary_PassesValidation(int rating)
    {
        var request = new CreateFeedbackRequest { ClassSessionId = 1, Rating = rating };

        var results = Validate(request);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(CreateFeedbackRequest.Rating)));
    }

    [Fact]
    public void ChatMessageCreateRequest_EmptyContent_FailsValidation()
    {
        var request = new ChatMessageCreateRequest { Content = "" };

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ChatMessageCreateRequest.Content)));
    }

    [Fact]
    public void ChatMessageCreateRequest_ContentOverMaxLength_FailsValidation()
    {
        var request = new ChatMessageCreateRequest { Content = new string('a', 2001) };

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ChatMessageCreateRequest.Content)));
    }

    [Fact]
    public void ApproveWithdrawalRequest_NoteShorterThanThreeChars_FailsValidation()
    {
        var request = new ApproveWithdrawalRequest
        {
            PaidAt = DateTimeOffset.UtcNow,
            Note = "ok",
            ProofImage = TestSupport.FakeFormFile("proof.jpg")
        };

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ApproveWithdrawalRequest.Note)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
