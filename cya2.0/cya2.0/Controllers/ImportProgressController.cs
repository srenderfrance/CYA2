using Microsoft.AspNetCore.Mvc;
using cya2.Services.Imports;

namespace cya2.Controllers
{
    [ApiController]
    [Route("api/import-progress")]
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
            return Ok(new { TotalRows = p.TotalRows, InsertedRows = p.InsertedRows, FailedRows = p.FailedRows, IsComplete = p.IsComplete });
        }
    }
}
