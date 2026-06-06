using Microsoft.AspNetCore.Mvc;

namespace MV.PresentationLayer.Controllers
{
    [ApiController]
    [Route("api/progress")]
    public class ProgressController : ControllerBase
    {
        //private readonly ICompletionRepository _completionRepository;

        //public ProgressController(ICompletionRepository completionRepository)
        //{
        //    _completionRepository = completionRepository;
        //}

        [HttpGet("subject-completion")]
        public async Task<IActionResult> GetSubjectCompletionPercentage([FromQuery] string studentId, [FromQuery] int subjectId)
        {
            if (string.IsNullOrEmpty(studentId) || subjectId <= 0)
            {
                return BadRequest(new { message = "ID học sinh và ID môn học là bắt buộc." });
            }

            try
            {
                //var percentage = await _completionRepository.CalculateSubjectCompletionPercentage(studentId, subjectId);
                return Ok(new
                {
                    studentId,
                    subjectId,
                    //completionPercentage = percentage
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi tính tiến độ học tập.", error = ex.Message });
            }
        }

        [HttpGet("overall-completion")]
        public async Task<IActionResult> GetOverallCompletionPercentage([FromQuery] string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                return BadRequest(new { message = "ID học sinh là bắt buộc." });
            }

            try
            {
                //var percentage = await _completionRepository.CalculateOverallCompletionPercentage(studentId);
                return Ok(new
                {
                    studentId,
                    //overallCompletionPercentage = percentage
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi khi tính tổng tiến độ học tập.", error = ex.Message });
            }
        }
    }
}
