using System;
using Microsoft.AspNetCore.Http;

namespace Marten.AspNetCore;

/// <summary>
/// Shared helpers for formatting and matching HTTP ETags across the streaming
/// result types (<see cref="StreamOne{T}"/>, <see cref="StreamAggregate{T}"/>).
/// </summary>
internal static class ETagHelpers
{
    /// <summary>
    /// Format a Marten document's <c>mt_version</c> (a <see cref="Guid"/>) as a quoted
    /// strong ETag value, e.g. <c>"3f2504e0-4f89-11d3-9a0c-0305e82c3301"</c>.
    /// </summary>
    public static string Format(Guid version) => $"\"{version:D}\"";

    /// <summary>
    /// Format an event stream's version (a <see cref="long"/>) as a quoted strong ETag
    /// value, e.g. <c>"17"</c>.
    /// </summary>
    public static string Format(long version) => $"\"{version}\"";

    /// <summary>
    /// Checks whether the incoming request's <c>If-None-Match</c> header contains a value
    /// that matches <paramref name="etag"/>. Handles the wildcard (<c>*</c>), multiple
    /// comma-separated values, and weak (<c>W/</c>) validators by comparing the underlying
    /// opaque tag.
    /// </summary>
    public static bool IfNoneMatchMatches(HttpContext context, string etag)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrEmpty(etag)) return false;

        var values = context.Request.Headers["If-None-Match"];
        if (values.Count == 0) return false;

        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value)) continue;

            foreach (var candidate in value.Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed.Length == 0) continue;

                if (trimmed == "*") return true;

                if (trimmed.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(2);
                }

                if (string.Equals(trimmed, etag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
