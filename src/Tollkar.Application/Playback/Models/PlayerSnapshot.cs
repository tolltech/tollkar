using Tollkar.Core.Playback;

namespace Tollkar.Application.Playback.Models;

public sealed record PlayerSnapshot(
    Guid? SongId,
    string? Title,
    string? Artist,
    PlaybackState State,
    TimeSpan Position)
{
    public static PlayerSnapshot Empty { get; } = new(null, null, null, PlaybackState.Stopped, TimeSpan.Zero);
}
