namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Where the entitlement currently in memory came from. Surfaced so the account screen can explain
/// itself rather than silently showing a subscriber the free tier.
/// </summary>
public enum SubscriptionVerificationState
{
    /// <summary>The server answered this session. The normal case.</summary>
    Verified,

    /// <summary>The server was unreachable and a still-valid cached snapshot is standing in.</summary>
    Cached,

    /// <summary>
    /// The server was unreachable and there is no usable cache — either none was stored, or the one
    /// stored has expired. Entitlement falls back to the free tier until the server can be reached.
    /// </summary>
    Unverified
}

/// <summary>
/// A snapshot of the server's subscription answer, persisted so an offline launch does not drop a
/// paying subscriber to the free tier.
///
/// The server is still authoritative — this is only consulted when it cannot be reached, and every
/// successful refresh overwrites it (including a negative answer, so a genuinely lapsed subscription
/// is not resurrected by a stale cache).
///
/// Trust is bounded two ways, because either guard alone leaves a hole:
///   - **The end dates.** They are what actually decides when access stops, so a cache whose
///     entitlement has run out is refused even if it was written moments ago.
///   - **A staleness cap.** The end date cannot be the only guard: the server may return an active
///     subscription with no EndDate at all, which would otherwise never expire, and a device clock
///     wound backwards would otherwise extend access for as long as the user stayed offline.
/// </summary>
public sealed record CachedSubscriptionStatus
{
    /// <summary>
    /// How long a snapshot may stand in for the server. Long enough to cover a normal stretch
    /// offline — a flight, a holiday, a dead cell area — without becoming an indefinite licence.
    /// </summary>
    public static readonly TimeSpan DefaultMaxStaleness = TimeSpan.FromDays(14);

    /// <summary>
    /// A snapshot timestamped in the future means the device clock moved backwards since it was
    /// written. Tolerate a little skew, then stop trusting it.
    /// </summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromHours(1);

    public bool HasActiveSubscription { get; init; }
    public string? SubscriptionStatus { get; init; }
    public DateTime? SubscriptionEndDate { get; init; }
    public bool IsOnTrial { get; init; }
    public DateTime? TrialEndDate { get; init; }
    public string? BillingSource { get; init; }

    /// <summary>When the server last confirmed this snapshot, in UTC.</summary>
    public DateTime CachedAtUtc { get; init; }

    /// <summary>
    /// True when this snapshot may stand in for the server: recent enough to trust, and describing
    /// entitlement that has not run out. A snapshot that fails either test is simply not applied,
    /// which leaves the caller on the free tier — the safe direction to fail in.
    /// </summary>
    public bool IsUsableAt(DateTime utcNow, TimeSpan? maxStaleness = null)
    {
        var age = utcNow - CachedAtUtc;

        if (age > (maxStaleness ?? DefaultMaxStaleness))
        {
            return false;
        }

        if (age < -ClockSkewTolerance)
        {
            return false;
        }

        return HasUnexpiredEntitlementAt(utcNow);
    }

    /// <summary>
    /// Either an unexpired subscription or an unexpired trial counts. Both are checked because a
    /// trial that has converted keeps <see cref="HasActiveSubscription"/> while dropping
    /// <see cref="IsOnTrial"/>, and refusing the snapshot on the spent trial alone would deny a
    /// subscriber who is genuinely paid up.
    /// </summary>
    private bool HasUnexpiredEntitlementAt(DateTime utcNow)
    {
        if (HasActiveSubscription && (SubscriptionEndDate is null || AsUtc(SubscriptionEndDate.Value) > utcNow))
        {
            return true;
        }

        return IsOnTrial && TrialEndDate is not null && AsUtc(TrialEndDate.Value) > utcNow;
    }

    /// <summary>
    /// Dates arriving from JSON often carry <see cref="DateTimeKind.Unspecified"/>. Treat those as
    /// UTC, matching what the server sends — reading them as local time would shift every
    /// comparison by the device's offset.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
