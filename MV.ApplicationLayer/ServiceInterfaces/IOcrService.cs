using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Interface chung cho các OCR Service (Google Cloud Vision, FPT AI, etc.)
    /// Dùng để trích xuất text từ ảnh chứng chỉ/bằng cấp
    /// </summary>
    public interface IOcrService
    {
        /// <summary>
        /// OCR generic để trích xuất text từ ảnh chứng chỉ/bằng cấp
        /// </summary>
        /// <param name="imageStream">Stream ảnh cần OCR</param>
        /// <param name="fileName">Tên file ảnh</param>
        /// <returns>OcrGenericResponse chứa text trích xuất và dữ liệu cấu trúc</returns>
        Task<OcrGenericResponse> ExtractTextFromImageAsync(Stream imageStream, string fileName);

        /// <summary>
        /// Xác thực issuer bằng AI - kiểm tra tổ chức có tồn tại và hợp lệ không
        /// </summary>
        /// <param name="issuer">Tên tổ chức cấp chứng chỉ</param>
        /// <returns>Kết quả xác thực từ AI</returns>
        Task<IssuerVerificationResult> VerifyIssuerAsync(string issuer);
    }
}
