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

    /// <summary>The problem <c>type</c> for a slug.</summary>
    public static string Uri(string slug) => Prefix + slug;
}