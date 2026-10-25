using System.Security.Cryptography;
using InventorySystem.Identity.Domain.Exceptions;

namespace InventorySystem.Identity.Domain.Security;

 
public sealed class PasswordHash : IEquatable<PasswordHash>
{
    /// <summary>The only algorithm this system writes.</summary>
    public const string Algorithm = "pbkdf2-sha256";

    private const char Separator = '$';
    private const int Parts = 4;

    private readonly byte[] _salt;
    private readonly byte[] _subkey;

    private PasswordHash(string encoded, int iterations, byte[] salt, byte[] subkey)
    {
        Encoded = encoded;
        Iterations = iterations;
        _salt = salt;
        _subkey = subkey;
    }

    public static PasswordHash None { get; } =
        new(string.Empty, iterations: 0, salt: [], subkey: []);

    public string Encoded { get; }

    public int Iterations { get; }

    public bool IsNone => Encoded.Length == 0;

    internal static PasswordHash Create(int iterations, byte[] salt, byte[] subkey) =>
        new(
            string.Join(Separator, Algorithm, iterations.ToString(System.Globalization.CultureInfo.InvariantCulture), ToBase64(salt), ToBase64(subkey)),
            iterations,
            salt,
            subkey);

 
    public bool Verify(string? password)
    {
        if (IsNone || string.IsNullOrEmpty(password) || _salt.Length == 0 || _subkey.Length == 0)
        {
            return false;
        }

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            password,
            _salt,
            Iterations,
            HashAlgorithmName.SHA256,
            _subkey.Length);

        return CryptographicOperations.FixedTimeEquals(candidate, _subkey);
    }

  
    public static PasswordHash Parse(string encoded)
    {
        if (TryParse(encoded, out var hash))
        {
            return hash;
        }

        throw new DomainException("The stored password hash is not in the format this system writes.");
    }

    public static bool TryParse(string? encoded, out PasswordHash hash)
    {
        hash = None;

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        var parts = encoded.Split(Separator);

        if (parts.Length != Parts || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1)
        {
            return false;
        }

        if (!TryFromBase64(parts[2], out var salt) || !TryFromBase64(parts[3], out var subkey))
        {
            return false;
        }

        if (salt.Length == 0 || subkey.Length == 0)
        {
            return false;
        }

        hash = new PasswordHash(encoded, iterations, salt, subkey);

        return true;
    }

    public bool Equals(PasswordHash? other) =>
        other is not null
        && Iterations == other.Iterations
        && string.Equals(Encoded, other.Encoded, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PasswordHash);

    public override int GetHashCode() => Encoded.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Encoded;

    private static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes);

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        var buffer = new byte[value.Length * 3 / 4 + 3];

        if (Convert.TryFromBase64String(value, buffer, out var written))
        {
            bytes = buffer[..written];

            return true;
        }

        bytes = [];

        return false;
    }
}
