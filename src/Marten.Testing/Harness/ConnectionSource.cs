using System;
using Weasel.Postgresql.Connections;

namespace Marten.Testing.Harness;

public class ConnectionSource: ConnectionFactory
{
    // ;Persist Security Info=true

    // Keep the default timeout pretty short
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("marten_testing_database")
                                                     ??
                                                     "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5";

    static ConnectionSource()
    {
        if (ConnectionString.IsEmpty())
            throw new Exception(
                "You need to set the connection string for your local Postgresql database in the environment variable 'marten_testing_database'");
    }


    public ConnectionSource(): base(new DefaultNpgsqlDataSourceFactory(), ConnectionString)
    {
    }
}
