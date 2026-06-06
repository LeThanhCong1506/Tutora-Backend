namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Kết quả xác thực Issuer bằng AI
    /// </summary>
    public class IssuerVerificationResult
    {
        /// <summary>
        /// Issuer có hợp lệ (tổ chức tồn tại, có uy tín) không
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Lý do tại sao issuer được coi là hợp lệ hoặc không hợp lệ
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Độ tin cậy của kết quả verification (0-100)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Tên chính thức của tổ chức (nếu AI xác định được)
        /// </summary>
        public string? OfficialName { get; set; }

        /// <summary>
        /// Loại tổ chức: university, certification_body, training_center, online_platform, etc.
        /// </summary>
        public string? OrganizationType { get; set; }
    }
}
