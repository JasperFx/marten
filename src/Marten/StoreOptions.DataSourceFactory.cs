using System;
using Npgsql;
using Weasel.Postgresql.Connections;

namespace Marten;

public partial class StoreOptions : INpgsqlDataSourceFactory
{
    public NpgsqlDataSource Create(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        if (LogFactory != null)
        {
            builder.UseLoggerFactory(LogFactory);
        }

        return builder.Build();
    }

    internal Func<string, NpgsqlDataSourceBuilder> NpgsqlDataSourceBuilderFactory
    {
        get => _npgsqlDataSourceBuilderFactory;
        private set => _npgsqlDataSourceBuilderFactory = value;
    }

    // Sets to itself in ctor
    internal INpgsqlDataSourceFactory NpgsqlDataSourceFactory { get; set; }
}
