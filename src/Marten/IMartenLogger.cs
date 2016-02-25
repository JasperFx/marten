using System;
using System.Threading.Tasks;
using Npgsql;

namespace Marten
{
    // Use this to inject default session loggers into DocumentSessions when opened
    // The default would be a Nullo. Set the global one on the StoreOptions
    public interface IMartenLogger
    {
        IMartenSessionLogger StartSession(IQuerySession session);

        void SchemaChange(string sql);
    }

    // This would be injected into a DocumentSession.
    // WILL expose IQuerySession.Logger as a settable property to do the 
    // easy contextual logging
    public interface IMartenSessionLogger
    {
        void LogSuccess(NpgsqlCommand command);
        void LogFailure(NpgsqlCommand command, Exception ex);

        // Called after a document session is saved/committed
        void RecordSavedChanges(IDocumentSession session);
    }

    public class NulloMartenLogger : IMartenLogger, IMartenSessionLogger
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
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
        }

        public void RecordSavedChanges(IDocumentSession session)
        {
        }
    }

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
            
        }
    }

}