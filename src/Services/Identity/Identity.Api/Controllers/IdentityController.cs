using System.Globalization;
using System.Security.Claims;
using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Identity.Application.Dtos;
using InventorySystem.Identity.Application.Exceptions;
using InventorySystem.Identity.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Identity.Api.Controllers;

/// Accounts and tokens.
///
/// POST /api/identity/login                sign in with a username and password → a token
/// POST /api/identity/token                a service presenting itself → a token
/// GET  /api/identity/me                   who the token belongs to
/// GET  /api/identity/users                the account directory (manager)
/// GET  /api/identity/users/{id}           one account (manager)
/// POST /api/identity/users                create an account (manager)
/// POST /api/identity/users/{id}/disable   revoke an account (manager)
/// POST /api/identity/users/{id}/enable    restore an account (manager)
///
/// The first two are the only anonymous ones. Everything else needs a token, and the directory needs
/// a manager's: who has access to the system is not something a clerk needs to know.

[ApiController]
[Route("api/identity")]
[Produces("application/json")]
public class IdentityController : ControllerBase
{
    private readonly SignInService _signIn;
    private readonly UserDirectory _directory;

    public IdentityController(SignInService signIn, UserDirectory directory)
    {
        _signIn = signIn;
        _directory = directory;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SignInResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SignInResponse>> Login(
        [FromBody] SignInRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _signIn.SignInAsync(request, cancellationToken));

    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ServiceToken), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ServiceToken>> Token(
        [FromBody] ServiceTokenRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _signIn.IssueServiceTokenAsync(request, cancellationToken));

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserSummary>> Me(CancellationToken cancellationToken)
    {
        var account = await _directory.FindAsync(SubjectId(), cancellationToken);

        // A token for an account that has since been removed is not a valid token any more, even
        // though its signature still checks out.
        return account is null
            ? Unauthorized(Problem("Account gone", "This token names an account that no longer exists."))
            : Ok(account);
    }

    [HttpGet("users")]
    [Authorize(Roles = JwtRoles.Manager)]
    [ProducesResponseType(typeof(IReadOnlyList<UserSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> Users(CancellationToken cancellationToken) =>
        Ok(await _directory.ListAsync(cancellationToken));

    [HttpGet("users/{id:int}")]
    [Authorize(Roles = JwtRoles.Manager)]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSummary>> GetUser(int id, CancellationToken cancellationToken) =>
        Ok(await _directory.GetAsync(id, cancellationToken));

    [HttpPost("users")]
    [Authorize(Roles = JwtRoles.Manager)]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserSummary>> Register(
        [FromBody] RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _directory.RegisterAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetUser), new { id = account.Id }, account);
    }

    [HttpPost("users/{id:int}/disable")]
    [Authorize(Roles = JwtRoles.Manager)]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSummary>> Disable(int id, CancellationToken cancellationToken) =>
        Ok(await _directory.SetActiveAsync(id, active: false, cancellationToken));

    [HttpPost("users/{id:int}/enable")]
    [Authorize(Roles = JwtRoles.Manager)]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSummary>> Enable(int id, CancellationToken cancellationToken) =>
        Ok(await _directory.SetActiveAsync(id, active: true, cancellationToken));

    /// >The account the token is about. The id is the only thing trusted here — the signature already checked.
    private int SubjectId()
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            throw new InvalidCredentialsException("This token does not name an account.");
        }

        return id;
    }

    private static ProblemDetails Problem(string title, string detail) => new()
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = title,
        Detail = detail
    };
}
