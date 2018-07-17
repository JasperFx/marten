using Marten.Schema;
using System;
using System.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Doc_1018
    {
        public Guid Id { get; set; }
        public string Field1 { get; set; }
        public string Field2 { get; set; }
    }

    public class Bug_1018_multi_key_unique_index_schema_update_assert_failure : IntegratedFixture
    {
        [Fact]
        public void check_database_matches_configuration_with_multi_key_unique_index()
        {
            var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Doc_1018>()
                    .Duplicate(x => x.Field1)
                    .Duplicate(x => x.Field2)
                    .UniqueIndex(UniqueIndexType.DuplicatedField, x => x.Field1, x => x.Field2);
            });

            store.Schema.ApplyAllConfiguredChangesToDatabase();
            store.Schema.AssertDatabaseMatchesConfiguration();
        }
    }
}
