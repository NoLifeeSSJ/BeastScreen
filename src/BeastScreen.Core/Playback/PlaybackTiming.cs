namespace BeastScreen.Core.Playback;

public static class PlaybackTiming
{
    /// <summary>Duration can be a moving estimate for radio. Use source length and seeking instead.</summary>
    public static bool IsLiveLikeNetworkStream(bool network, bool seekable, double sourceBytes, PlaybackState state) =>
        network && !seekable && sourceBytes <= 0 && state is PlaybackState.Playing or PlaybackState.Paused;
}
