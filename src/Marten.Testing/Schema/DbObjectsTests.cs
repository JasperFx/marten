using System;
using Baseline;
using Marten.Testing.Documents;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Schema
{
    public class DbObjectsTests : IntegratedFixture
    {
        private readonly ITestOutputHelper _output;

        public DbObjectsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void can_fetch_indexes_for_a_table_in_public()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            var store2 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.FirstName);
            });

            store2.Schema.EnsureStorageExists(typeof(User));

            var indices = store2.Schema.DbObjects.AllIndexes();

            indices.Each(x => _output.WriteLine(x.ToString()));

        }
    }
}