using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Mvc;

namespace CacheOrchestrator.Sample.Controllers;

[ApiController]
[CacheDomain("catalog")]
public sealed class ReportsController : ControllerBase
{
    [HttpGet("/api/reports/summary")]
    public IActionResult Summary()
    {
        return Ok(new
        {
            total = 42,
            generatedAt = DateTimeOffset.UtcNow
        });
    }
}