using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request model for creating multiple tutor availability slots at once
    /// </summary>
    public class BulkCreateAvailabilityRequest
    {
        /// <summary>
        /// List of availability slots to create
        /// </summary>
        [Required(ErrorMessage = "Availabilities list is required")]
        [MinLength(1, ErrorMessage = "At least one availability slot is required")]
        public List<CreateAvailabilityRequest> Availabilities { get; set; } = new();
    }
}
