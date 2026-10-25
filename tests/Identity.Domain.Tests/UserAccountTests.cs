using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Exceptions;
using InventorySystem.Identity.Domain.Security;

namespace InventorySystem.Identity.Domain.Tests;

/// <summary>
/// An account that exists is one that could sign in: every rule that could be broken is enforced at
/// the one moment an account is made, so nothing downstream has to wonder whether a row is usable.
/// </summary>
public class UserAccountTests
{
    private static readonly PasswordHasher Hasher = new(iterations: 1_000);

    private static readonly DateTime Created = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static User Register(
        string username = "dock.clerk",
        string displayName = "Dock clerk",
        UserRole role = UserRole.Clerk,
        string password = "dock-clerk-rota-2026") =>
        User.Register(username, displayName, role, Hasher.Hash(password), Created);

    [Fact]
    public void A_new_account_is_active_and_has_never_signed_in()
    {
        var user = Register();

        Assert.True(user.IsActive);
        Assert.Null(user.LastSignedInAt);
        Assert.Equal(Created, user.CreatedAt);
        Assert.Equal("dock.clerk", user.Username);
        Assert.Equal("Dock clerk", user.DisplayName);
        Assert.Equal(UserRole.Clerk, user.Role);
        Assert.False(user.IsService);
    }

    [Theory]
    [InlineData("Dock.Clerk", "dock.clerk")]
    [InlineData("  DOCK.CLERK  ", "dock.clerk")]
    [InlineData("clerk_2", "clerk_2")]
    [InlineData("2nd-shift", "2nd-shift")]
    public void A_username_is_normalised_so_two_spellings_are_one_account(string typed, string stored)
    {
        Assert.Equal(stored, Register(username: typed).Username);
        Assert.Equal(stored, User.NormaliseUsername(typed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]                                   // shorter than the minimum
    [InlineData(".clerk")]                               // does not start with a letter or a digit
    [InlineData("-shift")]                               // ditto
    [InlineData("dock clerk")]                           // a space would not survive a URL intact
    [InlineData("dock@clerk")]                           // punctuation nobody agreed to
    [InlineData("o'brien")]                              // an apostrophe
    [InlineData("clerk/../admin")]                       // a path, of all things
    [InlineData("clérk")]                                // accents normalise differently per machine
    [InlineData(null)]
    public void An_implausible_username_is_refused(string? username)
    {
        Assert.False(User.IsPlausibleUsername(username));
        Assert.Throws<DomainException>(() => Register(username: username!));
    }

    [Fact]
    public void A_username_that_is_far_too_long_is_refused()
    {
        Assert.True(User.IsPlausibleUsername(new string('a', User.UsernameMaxLength)));
        Assert.False(User.IsPlausibleUsername(new string('a', User.UsernameMaxLength + 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_account_without_a_display_name_is_refused(string? displayName)
    {
        var refusal = Assert.Throws<DomainException>(() => Register(displayName: displayName!));

        Assert.Contains("display name", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_display_name_is_trimmed_and_bounded()
    {
        Assert.Equal("Dock clerk", Register(displayName: "  Dock clerk  ").DisplayName);

        Assert.Throws<DomainException>(() => Register(displayName: new string('a', User.DisplayNameMaxLength + 1)));
    }

    [Fact]
    public void A_role_that_does_not_exist_is_refused()
    {
        var refusal = Assert.Throws<DomainException>(() => Register(role: (UserRole)99));

        Assert.Contains("not a role this system knows", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_account_must_be_created_with_a_password()
    {
        Assert.Throws<DomainException>(() => User.Register("dock.clerk", "Dock clerk", UserRole.Clerk, PasswordHash.None, Created));
        Assert.Throws<DomainException>(() => User.Register("dock.clerk", "Dock clerk", UserRole.Clerk, null, Created));
    }

    [Fact]
    public void Only_the_accounts_own_password_verifies_against_it()
    {
        var user = Register(password: "dock-clerk-rota-2026");

        Assert.True(user.VerifyPassword("dock-clerk-rota-2026"));
        Assert.False(user.VerifyPassword("dock-clerk-rota-2027"));
        Assert.False(user.VerifyPassword(null));
    }

    [Fact]
    public void Disabling_an_account_leaves_its_password_and_its_history_alone()
    {
        var user = Register();

        user.RecordSignIn(Created.AddHours(1));
        user.Disable();

        Assert.False(user.IsActive);
        Assert.Equal(Created.AddHours(1), user.LastSignedInAt);
        Assert.True(user.VerifyPassword("dock-clerk-rota-2026"));   // the credential is unchanged; the account is not

        user.Enable();

        Assert.True(user.IsActive);
    }

    [Fact]
    public void A_sign_in_is_recorded_in_utc()
    {
        var user = Register();
        var at = new DateTime(2026, 10, 16, 19, 51, 7, DateTimeKind.Utc);

        user.RecordSignIn(at);

        Assert.Equal(at, user.LastSignedInAt);
        Assert.Equal(DateTimeKind.Utc, user.LastSignedInAt!.Value.Kind);
    }

    [Fact]
    public void A_machine_account_is_a_service_and_a_person_is_not()
    {
        var relay = Register(username: "console", displayName: "Console relay", role: UserRole.Service);

        Assert.True(relay.IsService);
        Assert.False(Register(role: UserRole.Manager).IsService);
    }
}
