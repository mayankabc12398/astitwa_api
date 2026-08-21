using HrSuite.Common.Results;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// Base-code controllers. Everything about the envelope lives in <see cref="HrControllerBase"/>,
/// which plugin assemblies share; this adds only the host's route convention.
/// </summary>
[Route("api/[controller]")]
public abstract class ApiControllerBase : HrControllerBase
{
    protected IActionResult EnvelopeData(object? data) => Data(data);

    protected IActionResult Forbidden(string message) => Fail(ErrorCode.Forbidden, message);
}
