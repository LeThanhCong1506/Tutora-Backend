using System.Text.Json.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response từ Generic OCR API
    /// Trích xuất text từ ảnh chứng chỉ/bằng cấp
    /// </summary>
    public class OcrGenericResponse
    {
        /// <summary>
        /// Trích xuất thành công hay không
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Độ tin cậy của kết quả OCR (0-100)
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Toàn bộ text trích xuất được (raw)
        /// </summary>
        public string? RawText { get; set; }

        /// <summary>
        /// Dữ liệu đã được cấu trúc hóa cho chứng chỉ
        /// </summary>
        public CertificateOcrData? ExtractedData { get; set; }

        /// <summary>
        /// Message nếu có lỗi
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    public class CertificateOcrData
    {
        /// <summary>
        /// Tên chứng chỉ/bằng cấp
        /// </summary>
        public string? CertificateName { get; set; }

        /// <summary>
        /// Tên người sở hữu chứng chỉ
        /// </summary>
        public string? HolderName { get; set; }

        /// <summary>
        /// Tổ chức cấp
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// Ngày cấp (format: yyyy-MM-dd hoặc dd/MM/yyyy)
        /// </summary>
        public string? IssueDate { get; set; }

        /// <summary>
        /// Ngày hết hạn (nếu có)
        /// </summary>
        public string? ExpiryDate { get; set; }

        /// <summary>
        /// Mã chứng chỉ/credential ID
        /// </summary>
        public string? CredentialId { get; set; }

        /// <summary>
        /// Loại chứng chỉ phát hiện được: "degree", "language", "professional", "other"
        /// </summary>
        public string? DetectedType { get; set; }

        /// <summary>
        /// Độ tin cậy cho từng trường đã trích xuất
        /// </summary>
        public Dictionary<string, double>? FieldConfidences { get; set; }
    }
}
