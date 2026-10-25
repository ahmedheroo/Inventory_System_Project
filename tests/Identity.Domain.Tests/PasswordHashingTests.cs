using InventorySystem.Identity.Domain.Exceptions;
using InventorySystem.Identity.Domain.Security;

namespace InventorySystem.Identity.Domain.Tests;

/// <summary>
/// What a stored password is, and what it must never be. The tests are about the two properties the
/// system depends on: a password cannot be read back out of the database, and the only thing that
/// verifies a hash is the password it was made from.
/// </summary>
public class PasswordHashingTests
{
    /// <summary>A fast work factor: these tests are about the shape of the hash, not its cost.</summary>
    private const int Fast = 1_000;

    private static readonly PasswordHasher Hasher = new(Fast);

    [Fact]
    public void A_password_is_not_recoverable_from_what_is_stored()
    {
        var hash = Hasher.Hash("dock-clerk-rota-2026");

        Assert.DoesNotContain("dock-clerk", hash.Encoded, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("pbkdf2-sha256$", hash.Encoded, StringComparison.Ordinal);
        Assert.Equal(4, hash.Encoded.Split('$').Length);       // algorithm, iterations, salt, key
        Assert.True(hash.Verify("dock-clerk-rota-2026"));
    }

    [Fact]
    public void The_work_factor_travels_with_the_hash_so_it_can_be_raised_later()
    {
        var hash = new PasswordHasher(iterations: 4_096).Hash("a-long-enough-password");

        Assert.Equal(4_096, hash.Iterations);

        // Read back by somebody who does not know which work factor was used, and still verifiable.
        var stored = PasswordHash.Parse(hash.Encoded);

        Assert.Equal(4_096, stored.Iterations);
        Assert.True(stored.Verify("a-long-enough-password"));
    }

    [Fact]
    public void Two_hashes_of_one_password_are_different_and_both_verify()
    {
        var first = Hasher.Hash("the-same-password-twice");
        var second = Hasher.Hash("the-same-password-twice");

        // A salt that is unique per password is what stops two clerks with the same password from
        // having the same row, and stops one stolen answer answering for the other.
        Assert.NotEqual(first.Encoded, second.Encoded);
        Assert.True(first.Verify("the-same-password-twice"));
        Assert.True(second.Verify("the-same-password-twice"));
    }

    [Fact]
    public void Only_the_right_password_verifies()
    {
        var hash = Hasher.Hash("the-right-password-here");

        Assert.True(hash.Verify("the-right-password-here"));
        Assert.False(hash.Verify("the-right-password-herf"));   // one character off
        Assert.False(hash.Verify("THE-RIGHT-PASSWORD-HERE"));   // case matters
        Assert.False(hash.Verify("the-right"));
        Assert.False(hash.Verify(null));
        Assert.False(hash.Verify(string.Empty));
    }

    [Fact]
    public void The_placeholder_hash_verifies_nothing()
    {
        Assert.True(PasswordHash.None.IsNone);
        Assert.False(PasswordHash.None.Verify("anything at all"));
        Assert.False(PasswordHash.None.Verify(string.Empty));
    }

    [Fact]
    public void A_decoy_hash_matches_no_password_anybody_knows()
    {
        var hasher = new PasswordHasher(Fast);

        // The sign-in path verifies against this when no account matched. It has to be a real hash
        // that really fails, or a wrong username would be answered faster than a wrong password.
        var decoy = hasher.Decoy;

        Assert.False(decoy.IsNone);
        Assert.False(decoy.Verify("inventory-console-2026"));
        Assert.False(decoy.Verify(string.Empty));

        // And it is the same one every time, so the work is not done twice.
        Assert.Same(decoy, hasher.Decoy);
    }

    [Theory]
    [InlineData("pbkdf2-sha256$1000$not-base64$also-not-base64")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==$")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$a2V5")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$a2V5")]
    [InlineData("md5$1000$c2FsdA==$a2V5")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_stored_hash_that_is_not_in_our_format_is_refused_not_guessed(string? stored)
    {
        Assert.False(PasswordHash.TryParse(stored, out _));
        Assert.Throws<DomainException>(() => PasswordHash.Parse(stored!));
    }

    [Fact]
    public void The_default_work_factor_is_the_published_one()
    {
        // Pinned deliberately: the cost of a guess is a security decision, and it should not drift
        // silently when somebody edits a constant.
        Assert.Equal(210_000, PasswordHasher.DefaultIterations);
        Assert.Equal(16, PasswordHasher.SaltBytes);
        Assert.Equal(32, PasswordHasher.SubkeyBytes);

        var hash = new PasswordHasher().Hash("the-default-work-factor");

        Assert.Equal(PasswordHasher.DefaultIterations, hash.Iterations);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("elevenchars")]
    [InlineData("            ")]
    [InlineData("")]
    [InlineData(null)]
    public void A_password_that_does_not_meet_the_policy_is_refused(string? password)
    {
        Assert.False(PasswordHasher.IsAcceptable(password));

        var refusal = Assert.Throws<DomainException>(() => Hasher.Hash(password));

        Assert.Contains("at least 12 characters", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_of_a_hundred_emoji_is_still_a_password()
    {
        var password = string.Concat(Enumerable.Repeat("🔐", 100));

        Assert.True(PasswordHasher.IsAcceptable(password));
        Assert.True(Hasher.Hash(password).Verify(password));
    }

    [Fact]
    public void A_hasher_cannot_be_built_with_a_work_factor_of_nothing()
    {
        Assert.Throws<DomainException>(() => new PasswordHasher(iterations: 0));
        Assert.Throws<DomainException>(() => new PasswordHasher(iterations: -1));
    }
}
