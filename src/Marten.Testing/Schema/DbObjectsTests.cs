using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    [Collection("dbobjects")]
    public class DbObjectsTests : OneOffConfigurationsContext
    {
        [Fact]
        public void can_fetch_indexes_for_a_table_in_public()
        {
            var store1 = StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            }).As<DocumentStore>();

            store1.Tenancy.Default.EnsureStorageExists(typeof(User));

            var store2 = StoreOptions(_ =>
            {
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.FirstName);
            }).As<DocumentStore>();

            store2.Tenancy.Default.EnsureStorageExists(typeof(User));

            var indices = store2.Tenancy.Default.DbObjects.AllIndexes();

            indices.Any(x => Equals(x.Table, store1.Storage.MappingFor(typeof(User)).ToQueryableDocument().Table))
                .ShouldBeFalse();

            indices.Any(x => Equals(x.Table, store2.Storage.MappingFor(typeof(User)).ToQueryableDocument().Table))
                .ShouldBeTrue();
        }

        [Fact]
        public void can_fetch_the_function_ddl()
        {
            var store1 = StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            store1.Tenancy.Default.EnsureStorageExists(typeof(User));

            var upsert = store1.Storage.MappingFor(typeof(User)).As<DocumentMapping>().UpsertFunction;

            var functionBody = store1.Tenancy.Default.DbObjects.DefinitionForFunction(upsert);

            functionBody.Body.ShouldContain( "mt_doc_user");
        }

        public DbObjectsTests() : base("dbobjects")
        {
        }
    }
}
