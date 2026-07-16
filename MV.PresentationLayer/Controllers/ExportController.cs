using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.PresentationLayer.Authorization;
using System.Text;
using System.Xml.Serialization;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/export")]
    [ApiController]
    [Authorize]
    public class ExportController : ControllerBase
    {
        private readonly IExportService _exportService;

        public ExportController(IExportService exportService)
        {
            _exportService = exportService;
        }

        //// API 1: Xuất danh sách HỌC SINH
        //[HttpGet("students-xml")]
        //public async Task<IActionResult> ExportStudentsToXml()
        //{
        //    var data = await _exportService.GetStudentsForExportAsync();
        //    return GenerateXmlFileResult(data, "Students");
        //}

        //// API 2: Xuất danh sách PHỤ HUYNH
        //[HttpGet("parents-xml")]
        //public async Task<IActionResult> ExportParentsToXml()
        //{
        //    var data = await _exportService.GetParentsForExportAsync();
        //    return GenerateXmlFileResult(data, "Parents");
        //}

        //// API 3: Xuất MỘT MOCKTEST (theo ID)
        //[HttpGet("mocktest-xml/{testId}")]
        //public async Task<IActionResult> ExportMockTestToXml(int testId)
        //{
        //    try
        //    {
        //        var data = await _exportService.GetMockTestForExportAsync(testId);
        //        return GenerateXmlFileResult(data, $"MockTest_{testId}");
        //    }
        //    catch (KeyNotFoundException ex)
        //    {
        //        return NotFound(new { message = ex.Message });
        //    }
        //    catch (Exception ex)
        //    {
        //        // Bắt các lỗi chung khác
        //        return StatusCode(500, new { message = "An internal error occurred.", error = ex.Message });
        //    }
        //}


        private const string ExcelMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        [RequirePermission(Permissions.ExportData)]
        [HttpGet("students")]
        public async Task<IActionResult> ExportStudentsToExcel()
        {
            var fileBytes = await _exportService.GetStudentsForExportExcelAsync();
            string fileName = $"Export_Students_{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, ExcelMimeType, fileName);
        }

        [RequirePermission(Permissions.ExportData)]
        [HttpGet("parents")]
        public async Task<IActionResult> ExportParentsToExcel()
        {
            var fileBytes = await _exportService.GetParentsForExportExcelAsync();
            string fileName = $"Export_Parents_{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, ExcelMimeType, fileName);
        }

        [RequirePermission(Permissions.ExportData)]
        [HttpGet("mock-tests/{id}")]
        public async Task<IActionResult> ExportMockTestToExcel(int id)
        {
            try
            {
                var fileBytes = await _exportService.GetMockTestForExportExcelAsync(id);
                string fileName = $"Export_MockTest_{id}_{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd_HHmmss}.xlsx";
                return File(fileBytes, ExcelMimeType, fileName);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        // =====================================================
        // TUTOR EXPORT ENDPOINTS (M3 - Replace Zalo OA)
        // =====================================================

        /// <summary>
        /// Export tutor classSession reports to Excel
        /// </summary>
        [HttpGet("tutor/class-sessions")]
        [Authorize(Roles = UserRole.Tutor)]
        public async Task<IActionResult> ExportTutorClassSessionsToExcel(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var tutorId = GetCurrentUserId();
            var fileBytes = await _exportService.ExportTutorClassSessionReportsAsync(tutorId, fromDate, toDate);
            string fileName = $"ClassSessionReports_{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, ExcelMimeType, fileName);
        }

        /// <summary>
        /// Export tutor earnings/settlement history to Excel
        /// </summary>
        [HttpGet("tutor/earnings")]
        [Authorize(Roles = UserRole.Tutor)]
        public async Task<IActionResult> ExportTutorEarningsToExcel(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var tutorId = GetCurrentUserId();
            var fileBytes = await _exportService.ExportTutorEarningsAsync(tutorId, fromDate, toDate);
            string fileName = $"Earnings_{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, ExcelMimeType, fileName);
        }

        /// <summary>
        /// Export tutor feedback history to Excel
        /// </summary>
        [HttpGet("tutor/feedbacks")]
        [Authorize(Roles = UserRole.Tutor)]
        public async Task<IActionResult> ExportTutorFeedbacksToExcel(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var tutorId = GetCurrentUserId();
            var fileBytes = await _exportService.ExportTutorFeedbacksAsync(tutorId, fromDate, toDate);
            string fileName = $"Feedbacks_{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(fileBytes, ExcelMimeType, fileName);
        }

        /// <summary>
        /// Get current user ID from JWT token
        /// </summary>
        private string GetCurrentUserId()
        {
            return User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin tài khoản trong token.");
        }


        // Hàm helper để tạo file XML và trả về
        private IActionResult GenerateXmlFileResult(object data, string fileNamePrefix)
        {
            var serializer = new XmlSerializer(data.GetType());
            var stringWriter = new StringWriterWithEncoding(Encoding.UTF8);

            serializer.Serialize(stringWriter, data);

            string xmlData = stringWriter.ToString();
            byte[] fileBytes = Encoding.UTF8.GetBytes(xmlData);

            string fileName = $"Export_{fileNamePrefix}_{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMdd_HHmmss}.xml";
            return File(fileBytes, "application/xml", fileName);
        }

        // Class hỗ trợ để đảm bảo file XML là UTF-8
        private sealed class StringWriterWithEncoding : StringWriter
        {
            private readonly Encoding _encoding;

            public StringWriterWithEncoding(Encoding encoding)
            {
                _encoding = encoding;
            }

            public override Encoding Encoding => _encoding;
        }
    }
}
