using Tollkar.Application.Library;
using Tollkar.Application.Queue;

namespace Tollkar.Infrastructure;

public sealed record TollkarServices(
    ILibraryService Library,
    IPlaybackQueueService PlaybackQueue);
