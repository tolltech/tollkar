using Tollkar.Web.Authentication;

namespace Tollkar.Web.Tests;

/// <summary>The pending set is bounded, so these cover which code is dropped to make room.</summary>
public sealed class DevicePairingBudgetTests
{
    private readonly AdjustableTimeProvider time = new(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.FromHours(4)));
    private readonly DevicePairing pairing;

    public DevicePairingBudgetTests() => pairing = new(time);

    [Fact]
    public void ADisplayReplacesItsOwnOldestCodeInsteadOfBeingRefused()
    {
        var codes = CreateMany("tv", DevicePairing.MaxPendingRequestsPerClient + 1);

        Assert.Equal(PairingState.Unknown, pairing.Peek(codes[0]));
        Assert.All(codes.Skip(1), code => Assert.Equal(PairingState.Pending, pairing.Peek(code)));
    }

    [Fact]
    public void AConfirmationOutlivesCodesThatAreStillWaitingForOne()
    {
        var codes = CreateMany("tv", DevicePairing.MaxPendingRequestsPerClient);
        Assert.Equal(PairingState.Approved, pairing.Approve(codes[0], "alice"));

        CreateMany("tv", 1);

        Assert.Equal(PairingState.Approved, pairing.Peek(codes[0]));
        Assert.Equal(PairingState.Unknown, pairing.Peek(codes[1]));
    }

    [Fact]
    public void OneClientCannotPushOutAnother()
    {
        var display = CreateMany("tv", DevicePairing.MaxPendingRequestsPerClient);

        CreateMany("flood", DevicePairing.MaxPendingRequestsPerClient * 3);

        Assert.All(display, code => Assert.Equal(PairingState.Pending, pairing.Peek(code)));
    }

    [Fact]
    public void AFloodOfClientsCostsTheBusiestOneItsOldestCode()
    {
        var clients = DevicePairing.MaxPendingRequests / DevicePairing.MaxPendingRequestsPerClient;
        var codes = Enumerable.Range(0, clients)
            .SelectMany(client => CreateMany("client-" + client, DevicePairing.MaxPendingRequestsPerClient))
            .ToList();

        var arrival = CreateMany("display", 1)[0];

        Assert.Equal(PairingState.Pending, pairing.Peek(arrival));
        Assert.Single(codes, code => pairing.Peek(code) == PairingState.Unknown);
    }

    [Fact]
    public void ExpiredCodesReleaseTheBudgetOfTheirDisplay()
    {
        var expired = CreateMany("tv", DevicePairing.MaxPendingRequestsPerClient);
        time.Advance(DevicePairing.Lifetime + TimeSpan.FromSeconds(1));

        var reissued = CreateMany("tv", DevicePairing.MaxPendingRequestsPerClient);

        Assert.All(expired, code => Assert.Equal(PairingState.Unknown, pairing.Peek(code)));
        Assert.All(reissued, code => Assert.Equal(PairingState.Pending, pairing.Peek(code)));
    }

    private List<string> CreateMany(string client, int count) => Enumerable.Range(0, count).Select(_ =>
    {
        // Distinct creation times keep "oldest" unambiguous.
        time.Advance(TimeSpan.FromSeconds(1));
        return pairing.Create(client).UserCode;
    }).ToList();
}
