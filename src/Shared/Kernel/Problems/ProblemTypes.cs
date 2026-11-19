namespace Kernel.Problems;

/// <summary>RFC 9457 problem type slugs. The <c>type</c> is "/problems/&lt;slug&gt;" (spec §9).</summary>
public static class ProblemTypes
{
    /// <summary>The path every problem type starts with.</summary>
    public const string Prefix = "/problems/";

    /// <summary>The request failed validation.</summary>
    public const string Validation = "validation";

    /// <summary>The email or password is wrong, or the account is locked out.</summary>
    public const string InvalidCredentials = "invalid-credentials";

    /// <summary>The resource doesn't exist, or belongs to another tenant (spec §8: never 403).</summary>
    public const string NotFound = "not-found";

    /// <summary>The row changed since the client read it (a stale <c>version</c>), or retries ran out.</summary>
    public const string ConcurrencyConflict = "concurrency-conflict";

    /// <summary>Deactivation is blocked: a physical balance still has quantity or value (spec §6).</summary>
    public const string StockNotZero = "stock-not-zero";

    /// <summary>Turning a location's negative-stock toggle off is blocked: a balance there is below zero (spec §6).</summary>
    public const string NegativeBalancesExist = "negative-balances-exist";

    /// <summary>A posting was sent without an <c>Idempotency-Key</c> header (spec §6).</summary>
    public const string IdempotencyKeyMissing = "idempotency-key-missing";

    /// <summary>The <c>Idempotency-Key</c> was already used by a posting with a different request (spec §6, step 2).</summary>
    public const string IdempotencyKeyReused = "idempotency-key-reused";

    /// <summary>A physical balance would go below zero at a location whose negative-stock toggle is off (spec §6, step 8).</summary>
    public const string InsufficientStock = "insufficient-stock";

    /// <summary>A posting names an inactive item (spec §6).</summary>
    public const string InactiveItem = "inactive-item";

    /// <summary>A posting names an inactive location (spec §6).</summary>
    public const string InactiveLocation = "inactive-location";

    /// <summary>A later posting touched a balance of the document being reversed (spec §6): post a correcting document instead.</summary>
    public const string ReversalNotLatest = "reversal-not-latest";

    /// <summary>The document already has a reversal (spec §6; <c>UX_StockDocuments_Reverses</c>).</summary>
    public const string AlreadyReversed = "already-reversed";

    /// <summary>The document is itself a reversal, which can't be reversed (spec §6).</summary>
    public const string CannotReverseReversal = "cannot-reverse-reversal";

    /// <summary>The location already has an open stocktake (plan D12).</summary>
    public const string StocktakeOpenExists = "stocktake-open-exists";

    /// <summary>The stocktake isn't in a state that allows this (plan D10/D12).</summary>
    public const string StocktakeNotOpen = "stocktake-not-open";

    /// <summary>The problem <c>type</c> for a slug.</summary>
    public static string Uri(string slug) => Prefix + slug;
}