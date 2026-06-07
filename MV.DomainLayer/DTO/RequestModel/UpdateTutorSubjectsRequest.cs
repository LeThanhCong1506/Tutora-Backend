using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorSubjectsRequest
    {
        public List<TutorSubjectGradePriceRequest> SubjectGradePrices { get; set; } = new();
    }
}
