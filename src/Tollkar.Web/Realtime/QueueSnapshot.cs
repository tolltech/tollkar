using Tollkar.Application.Queue.Models;

namespace Tollkar.Web.Realtime;

public sealed record QueueSnapshot(long Version, IReadOnlyList<PlaybackQueueItem> Items);
