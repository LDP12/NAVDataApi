using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NAVDataApi.Data;
using NAVDataApi.Models;

namespace NAVDataApi.Controllers;

/// <summary>
/// Manages the calling client's sandbox dataset.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
[Produces("application/json")]
[Tags("Admin")]
public sealed class AdminController(INavRepository repository, NavSandboxStore sandboxes) : ControllerBase
{
    [HttpPost("reset")]
    [EndpointSummary("Discard your changes and restore the original seed data. Other clients are unaffected.")]
    public ActionResult<ResetResponse> Reset()
    {
        repository.Reset();
        return Ok(new ResetResponse("Your dataset has been reset to seed state.", repository.LastResetUtc));
    }

    [HttpGet("status")]
    [EndpointSummary("Your client name, record count, last reset and when your dataset expires.")]
    public ActionResult<DataStatusResponse> GetStatus()
    {
        var client = User.Identity?.Name;
        var info = client is null ? null : sandboxes.GetInfo(client);

        return Ok(new DataStatusResponse(
            client,
            repository.GetAll().Count,
            repository.LastResetUtc,
            info?.CreatedUtc,
            info?.ExpiresUtc));
    }
}
