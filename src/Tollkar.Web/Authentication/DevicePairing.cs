using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Tollkar.Web.Authentication;

public enum PairingState
{
    Unknown,
    Pending,
    Approved,
    Expired,
    Conflict
}

public sealed class PairingRequest(string client, string userCode, string deviceCode, DateTimeOffset expiresAt)
{
    private string? ownerId;

    public string Client { get; } = client;
    public string UserCode { get; } = userCode;
    public string DeviceCode { get; } = deviceCode;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public string? OwnerId => Volatile.Read(ref ownerId);

    internal bool Approve(string owner) =>
        Interlocked.CompareExchange(ref ownerId, owner, null) is null || OwnerId == owner;
}

/// <summary>Pairs a display that cannot type credentials with an account session confirmed on a phone.</summary>
public sealed class DevicePairing(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    // The creating endpoint is anonymous, so the pending set is bounded per client and overall.
    public const int MaxPendingRequests = 200;
    public const int MaxPendingRequestsPerClient = 5;
    private readonly ConcurrentDictionary<string, PairingRequest> requests = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>Always issues a code: a crowded server drops older ones rather than refusing a display.</summary>
    public PairingRequest Create(string client)
    {
        lock (gate)
        {
            RemoveExpired();
            while (RequestsOf(client).Count() >= MaxPendingRequestsPerClient)
                RemoveOldestOf(client);
            // Under a flood the client holding the most codes gives one up, not a waiting display.
            while (requests.Count >= MaxPendingRequests)
                RemoveOldestOf(BusiestClient());

            var request = new PairingRequest(client, FreeUserCode(),
                RandomNumberGenerator.GetHexString(32, lowercase: true), timeProvider.GetUtcNow() + Lifetime);
            requests[request.UserCode] = request;
            return request;
        }
    }

    public PairingState Peek(string userCode) => StateOf(Find(userCode));

    public PairingState Approve(string userCode, string ownerId)
    {
        var request = Find(userCode);
        var state = StateOf(request);
        if (state is PairingState.Pending)
            return request!.Approve(ownerId) ? PairingState.Approved : PairingState.Conflict;
        // Repeating the confirmation from the same account is the same confirmation, not a competing one.
        return state is PairingState.Approved && request!.OwnerId != ownerId ? PairingState.Conflict : state;
    }

    /// <summary>Drops the request so a code shown by an unexpected display stops being confirmable.</summary>
    public PairingState Reject(string userCode, string ownerId)
    {
        var request = Find(userCode);
        var state = StateOf(request);
        // Declining an unconfirmed code is anyone's call; revoking a confirmation belongs to its author.
        if (state is PairingState.Approved && request!.OwnerId != ownerId)
            return PairingState.Conflict;
        if (request is not null)
            requests.TryRemove(request.UserCode, out _);
        return state;
    }

    /// <summary>Consumes an approved request: the confirmation grants exactly one session.</summary>
    public PairingClaim Claim(string? userCode, string? deviceCode)
    {
        var request = Find(userCode);
        // An unmatched device code is indistinguishable from an unknown request on purpose.
        if (request is null || !SecretsMatch(request.DeviceCode, deviceCode))
            return new(PairingState.Unknown, null);

        var state = StateOf(request);
        if (state is not PairingState.Approved)
            return new(state, null);

        // Removing by value lets only one of several concurrent claims win.
        return requests.TryRemove(new KeyValuePair<string, PairingRequest>(request.UserCode, request))
            ? new(PairingState.Approved, request.OwnerId)
            : new(PairingState.Unknown, null);
    }

    private static bool SecretsMatch(string expected, string? actual) => actual is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private PairingRequest? Find(string? userCode) =>
        userCode is { Length: > 0 } && requests.TryGetValue(userCode, out var request) ? request : null;

    private PairingState StateOf(PairingRequest? request) => request switch
    {
        null => PairingState.Unknown,
        _ when request.ExpiresAt <= timeProvider.GetUtcNow() => PairingState.Expired,
        { OwnerId: null } => PairingState.Pending,
        _ => PairingState.Approved
    };

    private string FreeUserCode()
    {
        string userCode;
        do userCode = RandomNumberGenerator.GetHexString(16, lowercase: true);
        while (requests.ContainsKey(userCode));
        return userCode;
    }

    private IEnumerable<KeyValuePair<string, PairingRequest>> RequestsOf(string client) =>
        requests.Where(pair => pair.Value.Client == client);

    private string BusiestClient() => requests
        .GroupBy(pair => pair.Value.Client, StringComparer.Ordinal)
        .MaxBy(group => group.Count())!.Key;

    private void RemoveOldestOf(string client)
    {
        // A confirmation nobody collected yet outlives codes that are still waiting for one.
        var oldest = RequestsOf(client)
            .OrderBy(pair => pair.Value.OwnerId is not null)
            .ThenBy(pair => pair.Value.ExpiresAt)
            .First();
        requests.TryRemove(oldest);
    }

    private void RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var pair in requests.Where(pair => pair.Value.ExpiresAt <= now))
            requests.TryRemove(pair);
    }

    public sealed record PairingClaim(PairingState State, string? OwnerId);
}
