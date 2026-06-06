namespace MV.DomainLayer.DTO.RequestModel
{
    public class AdminUserFilterParameters : UserParameters
    {
        public string? SearchTerm { get; set; }
        public string? Role { get; set; }
        public int? Status { get; set; }
        public DateTime? CreatedFrom { get; set; }
        public DateTime? CreatedTo { get; set; }
    }
}
