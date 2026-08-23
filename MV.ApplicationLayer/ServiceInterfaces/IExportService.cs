using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IExportService
    {
        /// <summary>
        /// All student records as a JSON-serialisable list (admin export).
        /// </summary>
        Task<StudentExportListResponse> GetStudentsForExportAsync();

        /// <summary>
        /// All parent records as a JSON-serialisable list (admin export).
        /// </summary>
        Task<ParentExportListResponse> GetParentsForExportAsync();

        /// <summary>
        /// User records (any role — Student/Parent/Tutor, and Admin/Staff when
        /// <paramref name="includeInternalAccounts"/> is true) as a JSON-serialisable list,
        /// filtered by the same criteria as the CMS user list (admin/staff export).
        /// </summary>
        Task<UserExportListResponse> GetUsersForExportAsync(AdminUserFilterParameters parameters, bool includeInternalAccounts);

        /// <summary>
        /// Mock-test result data as a JSON-serialisable payload (admin export).
        /// </summary>
        Task<MockTestExportResponse> GetMockTestForExportAsync(int testId);

        /// <summary>
        /// Student records as an Excel (.xlsx) byte array for download.
        /// </summary>
        Task<byte[]> GetStudentsForExportExcelAsync();

        /// <summary>
        /// Parent records as an Excel (.xlsx) byte array for download.
        /// </summary>
        Task<byte[]> GetParentsForExportExcelAsync();

        /// <summary>
        /// User records (any role, filterable) as an Excel (.xlsx) byte array for download.
        /// </summary>
        Task<byte[]> GetUsersForExportExcelAsync(AdminUserFilterParameters parameters, bool includeInternalAccounts);

        /// <summary>
        /// Mock-test results as an Excel (.xlsx) byte array for download.
        /// </summary>
        Task<byte[]> GetMockTestForExportExcelAsync(int testId);

        // ── Tutor export methods ───────────────────────────────────────────

        /// <summary>
        /// Export tutor classSession reports to Excel, optionally filtered by date range.
        /// </summary>
        Task<byte[]> ExportTutorClassSessionReportsAsync(string tutorId, DateTime? fromDate, DateTime? toDate);

        /// <summary>
        /// Export tutor earnings/settlement history to Excel, optionally filtered by date range.
        /// </summary>
        Task<byte[]> ExportTutorEarningsAsync(string tutorId, DateTime? fromDate, DateTime? toDate);

        /// <summary>
        /// Export tutor feedback history to Excel, optionally filtered by date range.
        /// </summary>
        Task<byte[]> ExportTutorFeedbacksAsync(string tutorId, DateTime? fromDate, DateTime? toDate);
    }
}
