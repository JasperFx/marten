using System;
using Npgsql;

namespace Marten
{
    // Use this to inject default session loggers into DocumentSessions when opened
    public interface IMartenLogger
    {
        // Somehow, I'd like to be able to associate the description
        // with what the system is doing. In our web based systems, I'd
        // like to make that the URL of the request and maybe a user
        IMartenSessionLogger StartSession(string description);
    }

    // This would be injected into a DocumentSession.
    // Might expose IQuerySession.Logger as a settable property
    public interface IMartenSessionLogger
    {
        // Using the Func or Action allows us to optionally capture
        // performance timings or exceptions.
        T Log<T>(NpgsqlCommand command, Func<T> func);
        void Log(NpgsqlCommand command, Action action);
    }
}