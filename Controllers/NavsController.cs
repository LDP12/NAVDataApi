using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NAVDataApi.Data;
using NAVDataApi.Models;

namespace NAVDataApi.Controllers;

/// <summary>
/// Read, create and update NAV records in the calling client's dataset.
/// </summary>
[ApiController]
[Route("api/navs")]
[Authorize]
[Produces("application/json")]
[Tags("NAVs")]
public sealed class NavsController(INavRepository repository) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List NAV records, optionally filtered by fund code, ISIN and date range.")]
    public ActionResult<IReadOnlyList<NavRecord>> GetAll(
        [FromQuery] string? fundCode,
        [FromQuery] string? isin,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to) =>
        Ok(repository.GetAll(fundCode, isin, from, to));

    [HttpGet("latest")]
    [EndpointSummary("Latest NAV for each share class.")]
    public ActionResult<IReadOnlyList<NavRecord>> GetLatest([FromQuery] string? fundCode) =>
        Ok(repository.GetLatest(fundCode));

    [HttpGet("{id:int}")]
    [ProducesResponseType<NavRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<NavRecord> GetById(int id) =>
        repository.GetById(id) is { } record ? Ok(record) : NotFound();

    [HttpPost]
    [EndpointSummary("Create a NAV record.")]
    [ProducesResponseType<NavRecord>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public ActionResult<NavRecord> Create(NavRecordRequest request)
    {
        var result = repository.Create(request);
        if (result.Status == RepositoryStatus.Conflict)
            return ConflictProblem(result.Error);

        return Created($"/api/navs/{result.Record!.Id}", result.Record);
    }

    [HttpPut("{id:int}")]
    [EndpointSummary("Replace an existing NAV record.")]
    [ProducesResponseType<NavRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public ActionResult<NavRecord> Update(int id, NavRecordRequest request)
    {
        var result = repository.Update(id, request);
        return result.Status switch
        {
            RepositoryStatus.NotFound => NotFound(),
            RepositoryStatus.Conflict => ConflictProblem(result.Error),
            _ => Ok(result.Record)
        };
    }

    private ObjectResult ConflictProblem(string? detail) =>
        Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
}
