using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql.Connections;
using Xunit;

namespace CoreTests.Sessions;

public class SessionSetupTests
{
    [Fact]
    public async Task ShouldUseCustomDataSourceFactoryWhenDefined()
    {
        // Given
        var dataSourceFactory = new DummyNpgsqlDataSourceFactory();

        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        options.DatabaseSchemaName = nameof(SessionSetupTests).ToLower();

        options.DataSourceFactory(dataSourceFactory);

        await using var store = new DocumentStore(options);

        // When
        await using var documentSession = store.LightweightSession();
        await documentSession.Query<Target>().FirstOrDefaultAsync();

        // Then
        dataSourceFactory.WasCalled.ShouldBeTrue();
    }

    private class DummyNpgsqlDataSourceFactory: INpgsqlDataSourceFactory
    {
        public bool WasCalled { get; private set; }

        public NpgsqlDataSource Create(string connectionString)
        {
            WasCalled = true;
            return new NpgsqlDataSourceBuilder(connectionString).Build();
        }
    }
}
