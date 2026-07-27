using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Phân loại mức độ ưu tiên của tranh chấp bằng AI (Groq), dựa trên loại và nội dung khiếu nại.
    /// </summary>
    public interface IDisputeClassificationService
    {
        /// <summary>
        /// Trả về mức độ ưu tiên (low/medium/high) + lý do ngắn gọn.
        /// Throws when Groq is unavailable or returns an invalid response so Hangfire can retry the job.
        /// </summary>
        Task<DisputeClassificationResult> ClassifyAsync(string disputeType, string reason);
    }
}
