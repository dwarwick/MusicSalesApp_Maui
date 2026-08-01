using System.Globalization;

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

    // --- Persistence ---
    //
    // Deliberately hand-rolled rather than JSON. This snapshot is written on every successful status
    // refresh and read on every launch, in a Release build that is trimmed and fully AOT-compiled —
    // exactly the conditions where reflection-based serialization fails quietly, returning a
    // defaulted object rather than throwing. A defaulted snapshot looks like "no entitlement", which
    // is indistinguishable from an absent cache and would silently drop a subscriber to the free
    // tier. A fixed field order and explicit parsing cannot degrade that way, and is directly
    // testable.

    private const char FieldSeparator = '|';
    private const string FormatVersion = "v1";
    private const int FieldCount = 8;

    public string Serialize() => string.Join(FieldSeparator,
        FormatVersion,
        HasActiveSubscription ? "1" : "0",
        Encode(SubscriptionStatus),
        FormatDate(SubscriptionEndDate),
        IsOnTrial ? "1" : "0",
        FormatDate(TrialEndDate),
        Encode(BillingSource),
        CachedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    /// <summary>
    /// Returns false for anything it cannot read in full — a missing value, a version it does not
    /// recognise, or a malformed field. Callers treat that as "no cache", which costs the user a
    /// trip to the free tier until the server is reachable, rather than acting on half a snapshot.
    /// </summary>
    public static bool TryParse(string? value, out CachedSubscriptionStatus? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var fields = value.Split(FieldSeparator);
        if (fields.Length != FieldCount || fields[0] != FormatVersion)
        {
            return false;
        }

        if (!DateTime.TryParse(
                fields[7],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var cachedAtUtc))
        {
            return false;
        }

        if (!TryParseDate(fields[3], out var subscriptionEndDate) || !TryParseDate(fields[5], out var trialEndDate))
        {
            return false;
        }

        result = new CachedSubscriptionStatus
        {
            HasActiveSubscription = fields[1] == "1",
            SubscriptionStatus = Decode(fields[2]),
            SubscriptionEndDate = subscriptionEndDate,
            IsOnTrial = fields[4] == "1",
            TrialEndDate = trialEndDate,
            BillingSource = Decode(fields[6]),
            CachedAtUtc = cachedAtUtc.ToUniversalTime()
        };

        return true;
    }

    private static string FormatDate(DateTime? value)
        => value is null ? string.Empty : AsUtc(value.Value).ToString("O", CultureInfo.InvariantCulture);

    private static bool TryParseDate(string field, out DateTime? value)
    {
        if (string.IsNullOrEmpty(field))
        {
            value = null;
            return true;
        }

        if (DateTime.TryParse(field, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Keeps a server string containing the separator from corrupting the field layout.</summary>
    private static string Encode(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : Uri.EscapeDataString(value);

    private static string? Decode(string field)
        => string.IsNullOrEmpty(field) ? null : Uri.UnescapeDataString(field);
}
