using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Dữ liệu tra cứu dùng chung (public) cho FE đổ dropdown: môn học, khối lớp.
    /// </summary>
    public interface ILookupService
    {
        Task<List<SubjectResponse>> GetSubjectsAsync();
        Task<List<GradeLevelResponse>> GetGradeLevelsAsync();
    }
}
