using MV.DomainLayer.DTO.ResponseModel.Certificate;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Service xử lý Phase 2: Credentials Auto-Validation
    /// </summary>
    public interface ICertificateVerificationService
    {
        /// <summary>
        /// Tự động xác thực chứng chỉ dựa trên OCR và các quy tắc validation
        /// </summary>
        /// <param name="certificate">Entity chứng chỉ cần xác thực</param>
        /// <param name="userFullName">Tên đầy đủ của user để so sánh</param>
        /// <param name="certificateFileStream">Stream file chứng chỉ để OCR</param>
        /// <param name="fileName">Tên file</param>
        /// <returns>Kết quả validation</returns>
        Task<CertificateValidationResult> AutoValidateCertificateAsync(
            Tutorcertificate certificate,
            string userFullName,
            Stream certificateFileStream,
            string fileName);
    }
}
