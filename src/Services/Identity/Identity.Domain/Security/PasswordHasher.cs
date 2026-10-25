using System.Security.Cryptography;
using InventorySystem.Identity.Domain.Exceptions;

namespace InventorySystem.Identity.Domain.Security;

 
public sealed class PasswordHasher
{
    public const int DefaultIterations = 210_000;

    public const int MinimumLength = 12;

    public const int SaltBytes = 16;
    public const int SubkeyBytes = 32;

    private readonly Lazy<PasswordHash> _decoy;

    public PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 1)
        {
            throw new DomainException("A password hash cannot be computed with fewer than one iteration.");
        }

        Iterations = iterations;

        // Built once, lazily: the decoy is only ever needed by a sign-in that matched no account.
        _decoy = new Lazy<PasswordHash>(() => Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));
    }

    public int Iterations { get; }
 
    public PasswordHash Decoy => _decoy.Value;

    public PasswordHash Hash(string? password)
    {
        if (!IsAcceptable(password))
        {
            throw new DomainException(ExplainPolicy());
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        var subkey = Rfc2898DeriveBytes.Pbkdf2(
            password!,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            SubkeyBytes);

        return PasswordHash.Create(Iterations, salt, subkey);
    }

    public static bool IsAcceptable(string? password) =>
        password is { Length: >= MinimumLength } && password.Any(character => !char.IsWhiteSpace(character));

    public static string ExplainPolicy() =>
        $"A password must be at least {MinimumLength} characters long and must not be blank.";
}
