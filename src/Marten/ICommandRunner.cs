using System;
using System.Collections.Generic;
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
        IEnumerable<T> Query<T>(NpgsqlCommand cmd, ISerializer serializer);
    }
}