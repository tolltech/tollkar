namespace Tollkar.Core.Songs;

[Flags]
public enum SongCapabilities
{
    None = 0,
    Audio = 1 << 0,
    Video = 1 << 1,
    SyncedLyrics = 1 << 2,
    MultipleAudioTracks = 1 << 3,
    PitchChange = 1 << 4,
    TempoChange = 1 << 5
}
