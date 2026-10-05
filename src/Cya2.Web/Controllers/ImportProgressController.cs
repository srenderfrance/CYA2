using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using cya2.Services.Imports;
using Cya2.Application.Interfaces;

namespace cya2.Controllers
{
    [ApiController]
    [Route("api/import-progress")]
    [Authorize]
    [DisableRateLimiting]
    public class ImportProgressController : ControllerBase
    {
        private readonly ImportProgressService _progressService;
        private readonly IImportAuthorizationContext _authorizationContext;

        public ImportProgressController(
            ImportProgressService progressService,
            IImportAuthorizationContext authorizationContext)
        {
            _progressService = progressService;
            _authorizationContext = authorizationContext;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            var actor = await _authorizationContext.GetCurrentActorAsync();
            var p = actor is null ? null : _progressService.GetOwned(id, actor.UserId);
            if (p == null) return NotFound("The import is no longer available.");
            
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
