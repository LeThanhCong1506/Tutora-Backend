using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request model for deleting multiple tutor availability slots at once
    /// </summary>
    public class BulkDeleteAvailabilityRequest
    {
        /// <summary>
        /// List of availability IDs to delete
        /// </summary>
        [Required(ErrorMessage = "AvailabilityIds list is required")]
        [MinLength(1, ErrorMessage = "At least one availability ID is required")]
        public List<int> AvailabilityIds { get; set; } = new();
    }
}
