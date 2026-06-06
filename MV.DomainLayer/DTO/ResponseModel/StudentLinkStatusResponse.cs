namespace MV.DomainLayer.DTO.ResponseModel
{
    public class StudentLinkStatusResponse
    {
        public bool Linked { get; set; }
        public string? ParentName { get; set; }
        public string? ParentId { get; set; }
        public StudentProfileResponse? StudentProfile { get; set; }
    }
}
