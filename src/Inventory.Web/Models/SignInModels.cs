using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

/// <summary>
/// The sign-in form. The rules here are only about the shape of the request — a name and a password
/// were typed — because whether they are an account is the Identity service's answer to give, and its
/// refusal is what the page shows.
/// </summary>
public sealed class SignInForm
{
    [Required(ErrorMessage = "Enter your sign-in name.")]
    [Display(Name = "Sign-in name")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Where the visitor was going when the console turned them away. Checked before it is used.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>What the sign-in page is rendered from.</summary>
public sealed class SignInViewModel
{
    public SignInForm Form { get; init; } = new();

    /// <summary>Said above the form when the console refused what was typed, in the Identity service's own words.</summary>
    public string? Refusal { get; init; }
}
