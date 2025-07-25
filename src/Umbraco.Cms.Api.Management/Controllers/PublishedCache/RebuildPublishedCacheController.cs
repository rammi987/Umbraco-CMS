using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.PublishedCache;

namespace Umbraco.Cms.Api.Management.Controllers.PublishedCache;

[ApiVersion("1.0")]
public class RebuildPublishedCacheController : PublishedCacheControllerBase
{
    private readonly IDatabaseCacheRebuilder _databaseCacheRebuilder;

    public RebuildPublishedCacheController(IDatabaseCacheRebuilder databaseCacheRebuilder) => _databaseCacheRebuilder = databaseCacheRebuilder;

    [HttpPost("rebuild")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Rebuild(CancellationToken cancellationToken)
    {
        if (_databaseCacheRebuilder.IsRebuilding())
        {
            var problemDetails = new ProblemDetails
            {
                Title = "Database cache cannot be rebuilt",
                Detail = "The database cache is in the process of rebuilding.",
                Status = StatusCodes.Status400BadRequest,
                Type = "Error",
            };

            return Task.FromResult<IActionResult>(Conflict(problemDetails));
        }

        _databaseCacheRebuilder.Rebuild(true);
        return Task.FromResult<IActionResult>(Ok());
    }
}
