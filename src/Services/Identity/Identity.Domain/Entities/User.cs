using InventorySystem.Identity.Domain.Exceptions;
using InventorySystem.Identity.Domain.Security;

namespace InventorySystem.Identity.Domain.Entities;

 
public sealed class User
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 64;
    public const int DisplayNameMaxLength = 120;

    private User()
    {
    }

    private User(string username, string displayName, UserRole role, PasswordHash password, bool isActive, DateTime createdAt)
    {
        Username = username;
        DisplayName = displayName;
        Role = role;
        Password = password;
        IsActive = isActive;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }

    public string Username { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public PasswordHash Password { get; private set; } = PasswordHash.None;

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastSignedInAt { get; private set; }

    public bool IsService => Role == UserRole.Service;
 
    public static User Register(
        string? username,
        string? displayName,
        UserRole role,
        PasswordHash? password,
        DateTime createdAt)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException($"'{role}' is not a role this system knows.");
        }

        if (password is null || password.IsNone)
        {
            throw new DomainException("An account must be created with a password.");
        }

        return new User(
            NormaliseUsername(username),
            ValidateDisplayName(displayName),
            role,
            password,
            isActive: true,
            createdAt);
    }
 
    public bool VerifyPassword(string? password) => Password.Verify(password);

    public void RecordSignIn(DateTime at) => LastSignedInAt = at;

    public void Disable() => IsActive = false;

    public void Enable() => IsActive = true;
 
    public static string NormaliseUsername(string? username)
    {
        if (!IsPlausibleUsername(username))
        {
            throw new DomainException(ExplainUsername());
        }

        return username!.Trim().ToLowerInvariant();
    }
 
    public static bool IsPlausibleUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var candidate = username.Trim();

        if (candidate.Length < UsernameMinLength || candidate.Length > UsernameMaxLength)
        {
            return false;
        }

        if (!IsAsciiLetterOrDigit(candidate[0]))
        {
            return false;
        }

        return candidate.All(character =>
            IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    public static string ExplainUsername() =>
        $"A username is {UsernameMinLength} to {UsernameMaxLength} characters of letters, digits, "
        + "'.', '-' or '_', starting with a letter or a digit.";

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static string ValidateDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("An account needs a display name: it is what the console shows.");
        }

        var trimmed = displayName.Trim();

        if (trimmed.Length > DisplayNameMaxLength)
        {
            throw new DomainException($"A display name may not be longer than {DisplayNameMaxLength} characters.");
        }

        return trimmed;
    }
}
