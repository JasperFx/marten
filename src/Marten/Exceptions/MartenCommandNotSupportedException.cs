using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using JasperFx.Core.Exceptions;
using Npgsql;

namespace Marten.Exceptions;

/// <summary>
///     Reasons for feature not being supported
/// </summary>
public enum NotSupportedReason
{
    /// <summary>
    ///     Full Text Search needs at least Postgres version 10 - eg. doing PlainTextSearch or using to_tsvector
    /// </summary>
    FullTextSearchNeedsAtLeastPostgresVersion10 = 1,

    /// <summary>
    ///     Web Styles Search needs at least Postgres version 11
    /// </summary>
    WebStyleSearchNeedsAtLeastPostgresVersion11 = 2
}

/// <summary>
///     Informs that feature used in Postgres command is not supported
/// </summary>
public class MartenCommandNotSupportedException: MartenCommandException
{
    /// <summary>
    ///     Creates MartenCommandNotSupportedException based on the reason, command and innerException information with
    ///     formatted message.
    /// </summary>
    /// <param name="reason">reason for feature not being supported</param>
    /// <param name="command">failed Postgres command</param>
    /// <param name="innerException">internal exception details</param>
    /// <param name="message">optional additional exception information</param>
    public MartenCommandNotSupportedException(
        NotSupportedReason reason,
        NpgsqlCommand? command,
        Exception innerException,
        string? message = null
    ): base(command, innerException, message)
    {
        Reason = reason;
    }

    /// <summary>
    ///     Reason for feature not being supported
    /// </summary>
    public NotSupportedReason Reason { get; }
}

internal class MartenCommandNotSupportedExceptionTransform: IExceptionTransform
{
    public bool TryTransform(Exception original, [NotNullWhen(true)]out Exception? transformed)
    {
        if (original is NpgsqlException e)
        {
            var knownCause = KnownNotSupportedExceptionCause.KnownCauses.FirstOrDefault(x => x.Matches(e));
            if (knownCause != null)
            {
                var command = e.Data.Contains(nameof(NpgsqlCommand))
                    ? (NpgsqlCommand)e.Data[nameof(NpgsqlCommand)]!
                    : null;

                transformed =
                    new MartenCommandNotSupportedException(knownCause.Reason, command, e, knownCause.Description);

                return true;
            }
        }

        transformed = null;
        return false;
    }
}

internal sealed class KnownNotSupportedExceptionCause
{
    internal static readonly KnownNotSupportedExceptionCause ToTsvectorOnJsonb = new(
        "Full Text Search needs at least Postgres version 10.",
        NotSupportedReason.FullTextSearchNeedsAtLeastPostgresVersion10,
        e => e is PostgresException pe && pe.SqlState == PostgresErrorCodes.UndefinedFunction &&
             KnownNotSupportedExceptionCauseRegexExpressions.ToTsvectorOnJsonbRegex().IsMatch(pe.Message));

    internal static readonly KnownNotSupportedExceptionCause WebStyleSearch = new(
        "Full Text Search needs at least Postgres version 10.",
        NotSupportedReason.WebStyleSearchNeedsAtLeastPostgresVersion11,
        e => e is PostgresException pe && pe.SqlState == PostgresErrorCodes.UndefinedFunction &&
             KnownNotSupportedExceptionCauseRegexExpressions.WebStyleSearchRegex().IsMatch(pe.Message));

    internal static readonly KnownNotSupportedExceptionCause[] KnownCauses = { ToTsvectorOnJsonb, WebStyleSearch };
    private readonly Func<Exception, bool> match;

    internal KnownNotSupportedExceptionCause(string description, NotSupportedReason reason, Func<Exception, bool> match)
    {
        Description = description;
        Reason = reason;
        this.match = match;
    }

    internal NotSupportedReason Reason { get; }
    internal string Description { get; }

    internal bool Matches(Exception e)
    {
        return match(e);
    }

    private static partial class KnownNotSupportedExceptionCauseRegexExpressions
    {
        [GeneratedRegex(@"function to_tsvector\((?:regconfig, )?jsonb\) does not exist")]
        internal static partial Regex ToTsvectorOnJsonbRegex();

        [GeneratedRegex(@"function websearch_to_tsquery\((?:regconfig, )?text\) does not exist")]
        internal static partial Regex WebStyleSearchRegex();
    }
}
