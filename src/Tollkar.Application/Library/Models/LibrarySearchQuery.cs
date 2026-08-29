namespace Tollkar.Application.Library.Models;

public sealed record LibrarySearchQuery(string? Text = null, int Limit = 100)
{
    public const int MaximumLimit = 500;

    public int ValidatedLimit => Math.Clamp(Limit, 1, MaximumLimit);
}
