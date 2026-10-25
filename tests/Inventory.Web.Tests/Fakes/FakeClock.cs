namespace InventorySystem.Web.Tests.Fakes;

/// <summary>
/// A clock that only moves when a test moves it. The console's token cache lives or dies by the
/// passage of time, and a test that had to wait for it would be a test nobody runs.
/// </summary>
internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
