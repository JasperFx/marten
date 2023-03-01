using System;
using System.Linq;
using System.Threading.Tasks;
using Bug2135;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests.Bugs
{
    public class Bug_2135_open_session_should_use_provided_connection_string: BugIntegrationContext
    {
        // This covers GH-2145
        [Fact]
        public async Task pass_in_current_connection()
        {
            var newTargets = Target.GenerateRandomData(5).ToArray();

            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await using var session = theStore.LightweightSession(SessionOptions.ForConnection(conn));

            session.Store(newTargets);
            await session.SaveChangesAsync();
        }

        [Fact]
        public async Task should_use_provided_connection_string()
        {
            // Use separate store to create schema first
            using var initializationStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestEntity>();
            });

            await initializationStore.Advanced.Clean.DeleteAllDocumentsAsync();

            await using (var session = initializationStore.LightweightSession())
            {
                session.Store(new TestEntity { Name = "Test" });
                await session.SaveChangesAsync();
            }

            static string ThrowAlways() => throw new Exception("Should not use default connection");
            using var testStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.None;
                x.Schema.For<TestEntity>();
                x.Connection(ThrowAlways);
            });

            var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);
            builder.Timeout = 11;
            var connectionString = builder.ConnectionString;


            await using (var session =
                         await testStore.LightweightSerializableSessionAsync(SessionOptions.ForConnectionString(connectionString)))
            {
                new NpgsqlConnectionStringBuilder(session.Connection.ConnectionString).Timeout.ShouldBe(11);
                session.Store(new TestEntity { Name = "Test 2" });
                await session.SaveChangesAsync();
            }

            await using (var session =
                         await testStore.LightweightSerializableSessionAsync(SessionOptions.ForConnectionString(connectionString)))
            {
                new NpgsqlConnectionStringBuilder(session.Connection.ConnectionString).Timeout.ShouldBe(11);
                var entities = await session.Query<TestEntity>()
                    .ToListAsync();

                entities.Count.ShouldBe(2);
            }
        }
    }
}

namespace Bug2135
{
    public class TestEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
