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
        /// Mock-test results as an Excel (.xlsx) byte array for download.
        /// </summary>
        Task<byte[]> GetMockTestForExportExcelAsync(int testId);

        // ── Tutor export methods ───────────────────────────────────────────

        /// <summary>
        /// Export tutor lesson reports to Excel, optionally filtered by date range.
        /// </summary>
        Task<byte[]> ExportTutorLessonReportsAsync(string tutorId, DateTime? fromDate, DateTime? toDate);

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
