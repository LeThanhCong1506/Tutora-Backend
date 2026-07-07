namespace MV.DomainLayer.DTO.ResponseModel
{
    public class StaffPermissionsResponse
    {
        public string StaffId { get; set; } = string.Empty;
        public string? StaffFullName { get; set; }
        public List<string> PermissionKeys { get; set; } = new();
    }
}
