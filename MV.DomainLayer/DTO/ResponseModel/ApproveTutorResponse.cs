namespace MV.DomainLayer.DTO.ResponseModel
{
    public class ApproveTutorResponse
    {
        public string TutorName { get; set; } = string.Empty;
        public string IsApproved { get; set; } = string.Empty; // Sẽ trả về "Approved" hoặc "Rejected"
        public string Reason { get; set; } = string.Empty;
    }
}
