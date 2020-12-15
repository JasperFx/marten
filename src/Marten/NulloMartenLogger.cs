using System;
using System.Diagnostics;
using Marten.Services;
using Npgsql;

namespace Marten
{
    internal class NulloMartenLogger: IMartenLogger, IMartenSessionLogger
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

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
        }

        public static IMartenSessionLogger Flyweight { get; } = new NulloMartenLogger();
    }
}
