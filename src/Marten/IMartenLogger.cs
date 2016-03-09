using System;
using System.Diagnostics;
using System.Linq;
using Marten.Services;
using Npgsql;

namespace Marten
{
    // SAMPLE: IMartenLogger
    /// <summary>
    /// Records command usage, schema changes, and sessions within Marten
    /// </summary>
    public interface IMartenLogger
    {
        IMartenSessionLogger StartSession(IQuerySession session);

        void SchemaChange(string sql);
    }

    /// <summary>
    /// Use to create custom logging within an IQuerySession or IDocumentSession
    /// </summary>
    public interface IMartenSessionLogger
    {
        /// <summary>
        /// Log a command that executed successfully
        /// </summary>
        /// <param name="command"></param>
        void LogSuccess(NpgsqlCommand command);

        /// <summary>
        /// Log a command that failed
        /// </summary>
        /// <param name="command"></param>
        /// <param name="ex"></param>
        void LogFailure(NpgsqlCommand command, Exception ex);

        /// <summary>
        /// Called immediately after committing an IDocumentSession
        /// through SaveChanges() or SaveChangesAsync()
        /// </summary>
        /// <param name="session"></param>
        void RecordSavedChanges(IDocumentSession session);
    }
    // ENDSAMPLE

    public class NulloMartenLogger : IMartenLogger, IMartenSessionLogger
    {
        public IMartenSessionLogger StartSession(IQuerySession session)
        {
            return this;
        }

        public void SchemaChange(string sql)
        {
            Debug.WriteLine("Executing DDL change:");
            Debug.WriteLine(sql);
            Debug.WriteLine("");
        }

        public void LogSuccess(NpgsqlCommand command)
        {
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
        }

        public void RecordSavedChanges(IDocumentSession session)
        {
        }
    }

    // SAMPLE: ConsoleMartenLogger
    public class ConsoleMartenLogger : IMartenLogger, IMartenSessionLogger
    {
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
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            Console.WriteLine("Postgresql command failed!");
            Console.WriteLine(command.CommandText);
            Console.WriteLine(ex);
        }

        public void RecordSavedChanges(IDocumentSession session)
        {
            var lastCommit = session.LastCommit;
            Console.WriteLine(
                $"Persisted {lastCommit.Updated.Count()} updates, {lastCommit.Inserted.Count()} inserts, and {lastCommit.Deleted.Count()} deletions");
        }
    }
    // ENDSAMPLE

}