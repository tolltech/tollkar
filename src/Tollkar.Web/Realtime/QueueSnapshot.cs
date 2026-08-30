using Tollkar.Application.Queue.Models;

namespace Tollkar.Web.Realtime;

public sealed record QueueSnapshot(long Version, IReadOnlyList<PlaybackQueueItem> Items, Guid? CurrentItemId = null,
    PlaybackSnapshot? Playback = null);

public sealed record PlaybackSnapshot(long Revision, bool IsPlaying, double PositionSeconds);

public sealed record PlaybackCommand(string Action, long Revision, double PositionSeconds = 0);
