namespace Tollkar.Web.Catalog;

public sealed class LibrarySyncOptions
{
    public string SongsPath { get; set; } = "songs";

    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromHours(1);
}
