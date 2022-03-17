using System;
using System.Threading.Tasks;

using BugXXXX;

using Marten;
using Marten.Services;
using Marten.Testing.Harness;

using Shouldly;

using Weasel.Core;

using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_XXXX_open_session_should_use_provided_connection_string: BugIntegrationContext
    {
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

            await using (var session = initializationStore.OpenSession())
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

            await using (var session = await testStore.OpenSessionAsync(SessionOptions.ForConnectionString(ConnectionSource.ConnectionString)))
            {
                session.Store(new TestEntity { Name = "Test 2" });
                await session.SaveChangesAsync();
            }

            await using (var session = await testStore.OpenSessionAsync(SessionOptions.ForConnectionString(ConnectionSource.ConnectionString)))
            {
                var entities = await session.Query<TestEntity>()
                    .ToListAsync();

                entities.Count.ShouldBe(2);
            }
        }
    }
}

namespace BugXXXX
{
    public class TestEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
