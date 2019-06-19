using System;
using System.Collections.Generic;
using Marten.Services;
using Npgsql;

namespace Marten.Testing.Examples
{
    public class RecordingLogger: IMartenSessionLogger
    {
        public readonly IList<NpgsqlCommand> Commands = new List<NpgsqlCommand>();

        public void LogSuccess(NpgsqlCommand command)
        {
            Commands.Add(command);
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            Commands.Add(command);
        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            // do nothing
        }
    }
}
