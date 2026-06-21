namespace MV.DomainLayer.DTO.RequestModel
{
    public class UserParameters
    {
        const int MaxPageSize = 50;
        public int PageNumber { get; set; } = 1;

        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = (value > MaxPageSize) ? MaxPageSize : value;
        }

        // Sorting
        public string? OrderBy { get; set; } = "createdat_desc";

        /// <summary>
        /// Tìm theo fullname, email, phone, username (không phân biệt hoa thường).
        /// </summary>
        public string? SearchTerm { get; set; }

        public string? TeachingAreaCity { get; set; }

        public bool? IsPublic { get; set; }

        public string? SubscriptionType { get; set; }
    }
}
