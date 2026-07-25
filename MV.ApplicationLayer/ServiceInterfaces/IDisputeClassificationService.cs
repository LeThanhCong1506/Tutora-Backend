using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Phân loại mức độ ưu tiên của tranh chấp bằng AI (Groq), dựa trên loại và nội dung khiếu nại.
    /// </summary>
    public interface IDisputeClassificationService
    {
        /// <summary>
        /// Trả về mức độ ưu tiên (low/medium/high) + lý do ngắn gọn, hoặc null nếu AI không khả dụng/lỗi.
        /// Không bao giờ throw — caller có thể bỏ qua kết quả null mà không ảnh hưởng luồng chính.
        /// </summary>
        Task<DisputeClassificationResult?> ClassifyAsync(string disputeType, string reason);
    }
}
