using System;
using Weasel.Postgresql.Connections;

namespace Marten.Testing.Harness;

public class ConnectionSource: ConnectionFactory
{
    // Keep the default timeout pretty short
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("marten_testing_database")
                                                     ??
                                                     "Host=localhost;Database=postgres;User ID=consulteren;Password=6yczTKqYkzkR2z;Port=5432;Pooling=true;Include Error Detail=true;";

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
