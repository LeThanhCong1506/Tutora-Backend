using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MV.InfrastructureLayer.DBContext;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("internal")]
public class InternalBotController : ControllerBase
{
    private readonly AGORADbContext _context;

    public InternalBotController(AGORADbContext context)
    {
        _context = context;
    }

    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects()
    {
        var subjects = await _context.Subjects
            .Select(s => new { subjectId = s.Subjectid, name = s.Subjectname })
            .OrderBy(s => s.subjectId)
            .ToListAsync();

        return Ok(subjects);
    }
}
