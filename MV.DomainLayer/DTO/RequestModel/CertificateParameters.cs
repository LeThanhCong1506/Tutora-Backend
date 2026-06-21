using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class CertificateParameters
    {
        private const int MaxPageSize = 50;

        public int PageNumber { get; set; } = 1;

        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = (value > MaxPageSize) ? MaxPageSize : value;
        }

        /// <summary>
        /// Tìm theo tên / email gia sư.
        /// </summary>
        public string? SearchTerm { get; set; }

        /// <summary>
        /// Lọc theo trạng thái: pending_review | verified | rejected | all
        /// Mặc định: pending_review
        /// </summary>
        public string Status { get; set; } = CertificateStatus.PendingReview;

        /// <summary>
        /// createdat_desc (mặc định) | createdat_asc | tutorname_asc | tutorname_desc
        /// </summary>
        public string? OrderBy { get; set; } = "createdat_desc";
    }
}
