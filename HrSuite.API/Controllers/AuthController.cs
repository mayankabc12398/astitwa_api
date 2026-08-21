using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Envelope(await _auth.LoginAsync(request, ct));

    /// <summary>Echoes back who the current token belongs to. Used by the client on refresh.</summary>
    [HttpGet("me")]
    public IActionResult Me([FromServices] ITenantContext tenant)
        => EnvelopeData(new
        {
            tenant.UserId,
            tenant.UserName,
            tenant.TenantId,
            tenant.TenantCode,
            tenant.TenantName,
            tenant.EmployeeId,
            Roles = tenant.Roles,
            Permissions = tenant.Permissions
        });
}
