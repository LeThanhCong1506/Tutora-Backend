using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IFptAiService
    {
        /// <summary>
        /// [EXISTING] OCR để trích xuất thông tin từ CCCD
        /// </summary>
        Task<FptAiIdCardResponse> VerifyIdCardAsync(Stream imageStream, string fileName);

        /// <summary>
        /// [NEW] Kiểm tra Liveness của video (người thật, cử động thật)
        /// </summary>
        Task<FptAiLivenessResponse> CheckVideoLivenessAsync(Stream videoStream, string fileName);

        /// <summary>
        /// [NEW] Kiểm tra nội dung text có an toàn không (không chứa từ ngữ xúc phạm, nhạy cảm)
        /// </summary>
        Task<TextModerationResponse> CheckTextContentSafeAsync(string textContent);
    }
}
