#nullable enable
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using Marten.Services;
using Npgsql;

namespace Marten;

#region sample_IMartenLogger

/// <summary>
///     Records command usage, schema changes, and sessions within Marten
/// </summary>
public interface IMartenLogger
{
    /// <summary>
    ///     Called when the session is initialized
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    IMartenSessionLogger StartSession(IQuerySession session);

    /// <summary>
    ///     Capture any DDL executed at runtime by Marten
    /// </summary>
    /// <param name="sql"></param>
    void SchemaChange(string sql);
}

/// <summary>
///     Use to create custom logging within an IQuerySession or IDocumentSession
/// </summary>
public interface IMartenSessionLogger
{
    /// <summary>
    ///     Log a command that executed successfully
    /// </summary>
    /// <param name="command"></param>
    void LogSuccess(NpgsqlCommand command);

    /// <summary>
    ///     Log a command that failed
    /// </summary>
    /// <param name="command"></param>
    /// <param name="ex"></param>
    void LogFailure(NpgsqlCommand command, Exception ex);

    /// <summary>
    ///     Log a command that executed successfully
    /// </summary>
    /// <param name="batch"></param>
    void LogSuccess(NpgsqlBatch batch);

    /// <summary>
    ///     Log a batch that failed
    /// </summary>
    /// <param name="batch"></param>
    /// <param name="ex"></param>
    void LogFailure(NpgsqlBatch batch, Exception ex);

    /// <summary>
    /// Log a message for generic errors
    /// </summary>
    /// <param name="ex"></param>
    /// <param name="message"></param>
    /// <param name="batch"></param>
    void LogFailure(Exception ex, string message);


    /// <summary>
    ///     Called immediately after committing an IDocumentSession
    ///     through SaveChanges() or SaveChangesAsync()
    /// </summary>
    /// <param name="session"></param>
    /// <param name="commit"></param>
    void RecordSavedChanges(IDocumentSession session, IChangeSet commit);


    /// <summary>
    ///     Called just before a command is to be executed. Use this to create
    ///     performance logging of Marten operations
    /// </summary>
    /// <param name="command"></param>
    public void OnBeforeExecute(NpgsqlCommand command);

    /// <summary>
    ///     Called just before a command is to be executed. Use this to create
    ///     performance logging of Marten operations
    /// </summary>
    /// <param name="command"></param>
    public void OnBeforeExecute(NpgsqlBatch batch);
}

#endregion

#region sample_ConsoleMartenLogger

public class ConsoleMartenLogger: IMartenLogger, IMartenSessionLogger
{
    private Stopwatch? _stopwatch;

    public IMartenSessionLogger StartSession(IQuerySession session)
    {
        return this;
    }

    public void SchemaChange(string sql)
    {
        Console.WriteLine("Executing DDL change:");
        Console.WriteLine(sql);
        Console.WriteLine();
    }

    public void LogSuccess(NpgsqlCommand command)
    {
        Console.WriteLine(command.CommandText);
        foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
            Console.WriteLine($"  {p.ParameterName}: {GetParameterValue(p)}");
    }

    public void LogSuccess(NpgsqlBatch batch)
    {
        foreach (var command in batch.BatchCommands)
        {
            Console.WriteLine(command.CommandText);
            foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
                Console.WriteLine($"  {p.ParameterName}: {GetParameterValue(p)}");
        }
    }

    private static object? GetParameterValue(NpgsqlParameter p)
    {
        if (p.Value is IList enumerable)
        {
            var result = "";
            for (var i = 0; i < Math.Min(enumerable.Count, 5); i++)
            {
                result += $"[{i}] {enumerable[i]}; ";
            }
            if (enumerable.Count > 5) result += $" + {enumerable.Count - 5} more";
            return result;
        }
        return p.Value;
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        Console.WriteLine("Postgresql command failed!");
        Console.WriteLine(command.CommandText);
        foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
            Console.WriteLine($"  {p.ParameterName}: {p.Value}");
        Console.WriteLine(ex);
    }

    public void LogFailure(NpgsqlBatch batch, Exception ex)
    {
        Console.WriteLine("Postgresql command failed!");
        foreach (var command in batch.BatchCommands)
        {
            Console.WriteLine(command.CommandText);
            foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
                Console.WriteLine($"  {p.ParameterName}: {p.Value}");
        }

        Console.WriteLine(ex);
    }

    public void LogFailure(Exception ex, string message)
    {
        Console.WriteLine("Failure: " + message);
        Console.WriteLine(ex.ToString());
    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        _stopwatch?.Stop();

        var lastCommit = commit;
        Console.WriteLine(
            $"Persisted {lastCommit.Updated.Count()} updates in {_stopwatch?.ElapsedMilliseconds ?? 0} ms, {lastCommit.Inserted.Count()} inserts, and {lastCommit.Deleted.Count()} deletions");
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {
        _stopwatch = new Stopwatch();
        _stopwatch.Start();
    }

    public void OnBeforeExecute(NpgsqlBatch batch)
    {
        _stopwatch = new Stopwatch();
        _stopwatch.Start();
    }
}

#endregion
