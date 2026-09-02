using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Npgsql;

namespace Marten.Exceptions;
#nullable enable

/// <summary>
///     Wraps the Postgres command exceptions. Unifies exception handling and brings additonal information.
/// </summary>
public class MartenCommandException: MartenException
{
    public const string MaybeLockedRowsMessage =
        "Postgresql timed out while trying to read data. This may be caused by trying to read locked rows";

    /// <summary>
    ///     The most statements from a failed <see cref="NpgsqlBatch" /> that are written into the exception
    ///     message when PostgreSQL did not tell us which one it rejected. A single projection page can carry
    ///     hundreds of operations, and dumping all of them turns one failure into an unreadable log entry.
    /// </summary>
    internal const int MaximumRenderedBatchCommands = 5;

    /// <summary>
    ///     Creates MartenCommandException based on the command and innerException information with formatted message.
    /// </summary>
    /// <param name="command">failed Postgres command</param>
    /// <param name="innerException">internal exception details</param>
    public MartenCommandException(NpgsqlCommand? command, Exception innerException)
        : base(ToMessage(command, innerException) + innerException.Message, innerException)
    {
        CommandText = ResolveCommandText(command, innerException);

        if (command == null)
            return;

        Command = new NpgsqlCommand
        {
            CommandText = command.CommandText,
            CommandType = command.CommandType,
            CommandTimeout = command.CommandTimeout
        };

        foreach (NpgsqlParameter parameter in command.Parameters)
        {
            Command.Parameters.Add(parameter.Clone());
        }
    }

    /// <summary>
    ///     Creates MartenCommandException based on the command and innerException information with formatted message.
    /// </summary>
    /// <param name="command">failed Postgres command</param>
    /// <param name="innerException">internal exception details</param>
    /// <param name="prefix">prefix that will be added to message</param>
    public MartenCommandException(
        NpgsqlCommand? command,
        Exception innerException,
        string? prefix
    ): base(ToMessage(command, innerException, prefix) + innerException.Message, innerException)
    {
        CommandText = ResolveCommandText(command, innerException);

        if (command == null)
            return;

        Command = new NpgsqlCommand
        {
            CommandText = command.CommandText,
            CommandType = command.CommandType,
            CommandTimeout = command.CommandTimeout
        };

        foreach (NpgsqlParameter parameter in command.Parameters)
        {
            Command.Parameters.Add(parameter);
        }
    }

    /// <summary>
    ///     Failed Postgres command
    /// </summary>
    public NpgsqlCommand? Command { get; }

    /// <summary>
    ///     The SQL Marten was able to recover for the failed statement. On the batched write path
    ///     (<c>ExecuteBatchPagesAsync</c>) there is no single <see cref="NpgsqlCommand" /> to report,
    ///     so this is sourced from the failing <see cref="NpgsqlBatchCommand" /> instead and is the
    ///     only place the SQL is available. Null only when nothing could be recovered.
    /// </summary>
    public string? CommandText { get; }

    protected static string ToMessage(
        NpgsqlCommand? command,
        Exception innerException,
        string? prefix = null
    )
    {
        if (prefix != null)
        {
            prefix = $"{prefix}${Environment.NewLine}";
        }

        var explanation = "";

        if (innerException is NpgsqlException
            {
                InnerException: TimeoutException { Message: "Timeout during reading attempt" }
            })
        {
            explanation = Environment.NewLine + MaybeLockedRowsMessage + Environment.NewLine;
        }

        return
            $"Marten Command Failure:${Environment.NewLine}{prefix}{explanation}{ResolveCommandText(command, innerException)}${Environment.NewLine}${Environment.NewLine}";
    }

    /// <summary>
    ///     Recover the SQL for a failed command. The batched write path used by SaveChangesAsync and by
    ///     every async projection has no single NpgsqlCommand to hand us, so before this fell back to
    ///     rendering nothing at all and the SQL was unrecoverable from the exception. Sources, in order:
    ///     the command itself, the batch statement PostgreSQL actually rejected, and finally the batch
    ///     that was recorded on the exception by <see cref="MartenExceptionTransformer.WrapAndThrow(NpgsqlBatch, Exception)" />.
    /// </summary>
    internal static string? ResolveCommandText(NpgsqlCommand? command, Exception innerException)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(command?.CommandText))
            {
                return command!.CommandText;
            }

            // Npgsql hands back the exact statement the server rejected whenever the failure came out
            // of a batch execution, which makes this the precise answer rather than a guess.
            if (innerException is NpgsqlException { BatchCommand: not null } npgsql &&
                !string.IsNullOrWhiteSpace(npgsql.BatchCommand.CommandText))
            {
                return npgsql.BatchCommand.CommandText;
            }

            return DescribeBatch(innerException.ReadNpgsqlBatch());
        }
        catch (Exception)
        {
            // Building a diagnostic message must never be the thing that throws. Losing the SQL is
            // bad; replacing the real failure with a formatting error is worse.
            return null;
        }
    }

    private static string? DescribeBatch(NpgsqlBatch? batch)
    {
        if (batch == null)
        {
            return null;
        }

        // NpgsqlBatchCommandCollection is not generically enumerable, so index it directly.
        var texts = new List<string>(batch.BatchCommands.Count);
        for (var i = 0; i < batch.BatchCommands.Count; i++)
        {
            var text = batch.BatchCommands[i].CommandText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
            }
        }

        if (texts.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var text in texts.Take(MaximumRenderedBatchCommands))
        {
            builder.Append(text.TrimEnd());
            builder.Append(Environment.NewLine);
        }

        if (texts.Count > MaximumRenderedBatchCommands)
        {
            builder.Append(
                $"-- and {texts.Count - MaximumRenderedBatchCommands} more statement(s) in this batch");
            builder.Append(Environment.NewLine);
        }

        return builder.ToString().TrimEnd();
    }
}
