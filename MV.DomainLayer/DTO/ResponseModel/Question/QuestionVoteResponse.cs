namespace MV.DomainLayer.DTO.ResponseModel.Question;

/// <summary>Kết quả sau khi user vote 1 câu hỏi.</summary>
public class QuestionVoteResponse
{
    public Guid QuestionId { get; set; }
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }
    public int HelpfulPercent { get; set; }

    /// <summary>Vote hiện tại của user: 1 | -1 | null (đã bỏ vote).</summary>
    public int? MyVote { get; set; }
}
