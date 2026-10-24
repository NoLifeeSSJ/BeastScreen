namespace BeastScreen.Core.Playback;

public sealed class ReconnectBudget
{
    public const int MaximumAttempts = 3;
    public int Attempts { get; private set; }
    public TimeSpan? TakeDelay()
    {
        if (Attempts >= MaximumAttempts) return null;
        return TimeSpan.FromSeconds(1 << Attempts++);
    }
    public void Reset() => Attempts = 0;
}
