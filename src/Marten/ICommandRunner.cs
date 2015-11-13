using System;
using System.Collections.Generic;
using Marten.Map;
using Npgsql;

namespace Marten
{
    public interface ICommandRunner
    {
        void Execute(Action<NpgsqlConnection> action);
        T Execute<T>(Func<NpgsqlConnection, T> func);
        IEnumerable<string> QueryJson(NpgsqlCommand cmd);
        int Execute(string sql);
        T QueryScalar<T>(string sql);
    }
}