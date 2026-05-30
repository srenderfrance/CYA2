using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using cya2.Services.Imports;

namespace cya2.Controllers
{
    [ApiController]
    [Route("api/import-progress")]
    [Authorize]
    [DisableRateLimiting]
    public class ImportProgressController : ControllerBase
    {
        private readonly ImportProgressService _progressService;

        public ImportProgressController(ImportProgressService progressService)
        {
            _progressService = progressService;
        }

        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            var p = _progressService.Get(id);
            if (p == null) return NotFound();
            
            object steps;
            if (p.Steps != null)
            {
                steps = p.Steps.Select(s => new {
                    Name = s.Name,
                    Status = s.Status,
                    IsCompleted = s.IsCompleted,
                    IsActive = s.IsActive,
                    Details = s.Details
                }).ToList();
            }
            else
            {
                steps = new List<object>();
            }
            
            return Ok(new { 
                TotalRows = p.TotalRows, 
                InsertedRows = p.InsertedRows, 
                FailedRows = p.FailedRows, 
                ExpectedRows = p.ExpectedRows, 
                Status = p.Status, 
                IsComplete = p.IsComplete,
                Errors = p.Errors ?? new List<string>(),
                Steps = steps
            });
        }
    }
}
