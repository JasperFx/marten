#nullable enable
using System;
using System.Text.RegularExpressions;

namespace Marten.Schema.Indexing.FullText;

/// <summary>
///     The single validator for a PostgreSQL text-search configuration name, for every place Marten
///     interpolates one into SQL.
/// </summary>
/// <remarks>
///     <para>
///         <c>regConfig</c> is interpolated rather than parameterized on purpose — binding it ruins
///         the query plan — so every interpolation site is a SQL injection sink and every one of them
///         must validate. GHSA-vmw2-qwm8-x84c (CVE-2026-45288) fixed the WHERE path by adding this
///         check, but it lived as a <c>private static</c> inside <c>FullTextWhereFragment</c>. When
///         the <c>ts_rank</c> ordering path arrived in 9.31.0 it could not reuse the check and did not
///         re-implement it, so the vulnerability came straight back on a new path
///         (GHSA-frqq-p5g3-8jq5).
///     </para>
///     <para>
///         Hence a shared type rather than a second call site: the check is now reachable from
///         anywhere, and <see cref="FullTextIndexResolver.ResolveVector" /> — the sink every search
///         path funnels through — validates on the way in, so a future caller is covered by
///         construction rather than by remembering.
///     </para>
/// </remarks>
internal static class RegConfigValidation
{
    // PostgreSQL text-search configuration names are stored as identifiers in pg_ts_config (see
    // https://www.postgresql.org/docs/current/textsearch-configuration.html). Simple unquoted
    // identifiers, optionally schema-qualified, so "english", "french" and "pg_catalog.english"
    // pass, while anything containing whitespace, a quote, a semicolon, or any other punctuation is
    // rejected. Each side is capped at NAMEDATALEN-1 (63).
    private static readonly Regex _pattern = new(
        @"^[a-zA-Z_][a-zA-Z0-9_]{0,62}(\.[a-zA-Z_][a-zA-Z0-9_]{0,62})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Throws unless <paramref name="regConfig" /> is a simple PostgreSQL identifier, optionally
    ///     schema-qualified.
    /// </summary>
    /// <param name="regConfig">The text-search configuration name about to be interpolated into SQL.</param>
    /// <param name="parameterName">
    ///     The caller's parameter name, so the <see cref="ArgumentException" /> points at the argument
    ///     the user actually passed rather than at an internal field.
    /// </param>
    internal static void Validate(string? regConfig, string parameterName = "regConfig")
    {
        if (regConfig is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (!_pattern.IsMatch(regConfig))
        {
            throw new ArgumentException(
                $"Invalid PostgreSQL text-search configuration name '{regConfig}'. " +
                "regConfig must be a simple PostgreSQL identifier (optionally schema-qualified), " +
                "matching ^[a-zA-Z_][a-zA-Z0-9_]*(\\.[a-zA-Z_][a-zA-Z0-9_]*)?$.",
                parameterName);
        }
    }
}
