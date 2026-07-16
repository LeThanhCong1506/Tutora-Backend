namespace MV.DomainLayer.DTO.ResponseModel
{
    public class PermissionCatalogItemResponse
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IReadOnlyList<string> Requires { get; set; } = Array.Empty<string>();

        // Compatibility with clients that still group by the old field.
        public string Group { get; set; } = string.Empty;
    }
}