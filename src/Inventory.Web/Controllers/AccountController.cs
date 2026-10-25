using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Models;
using Inventory.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;


public class AccountController : Controller
{
    private readonly IIdentityApi _identity;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IIdentityApi identity, ILogger<AccountController> logger)
    {
        _identity = identity;
        _logger = logger;
    }

    [HttpGet("sign-in")]
    [AllowAnonymous]
    public IActionResult SignIn(string? returnUrl = null)
    {
        // Already signed in and asking again: go where they were going rather than show the form.
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(SafeReturnUrl(returnUrl));
        }

        return View(new SignInViewModel { Form = new SignInForm { ReturnUrl = returnUrl } });
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignIn(SignInForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(new SignInViewModel { Form = form });
        }

        try
        {
            var signedIn = await _identity.SignInAsync(
                new LoginRequest(form.Username.Trim(), form.Password),
                cancellationToken);

            await HttpContext.SignInAsync(ConsoleSession.Scheme, ConsoleSession.PrincipalFor(signedIn));

            _logger.LogInformation(
                "{Username} signed in as {Role}.",
                signedIn.User.Username,
                signedIn.User.Role);

            return Redirect(SafeReturnUrl(form.ReturnUrl));
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The Identity service's own sentence, repeated rather than replaced: it knows whether the
            // account is disabled, and this page does not.
            _logger.LogInformation("Refused a sign-in for {Username}.", form.Username);

            ModelState.Clear();

            return View(new SignInViewModel { Form = form, Refusal = exception.Explain() });
        }
    }

    [HttpPost("sign-out")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutAsync()
    {
        _logger.LogInformation("{Username} signed out.", User.Identity?.Name);

        await HttpContext.SignOutAsync(ConsoleSession.Scheme);

        return RedirectToAction(nameof(SignIn));
    }

    [HttpGet("sign-in/denied")]
    [AllowAnonymous]
    public IActionResult Denied() => View();

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";
}
