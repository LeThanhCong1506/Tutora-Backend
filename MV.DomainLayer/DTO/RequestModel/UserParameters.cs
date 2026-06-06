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
        public string? OrderBy { get; set; } = "createdat_desc"; // Default sort
    }
}
