using System.Collections.Generic;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class ability_to_add_custom_storage_features : IntegrationContext
    {
        [Fact]
        public void can_register_a_custom_feature()
        {
            StoreOptions(_ =>
            {
                _.Storage.Add<FakeStorage>();
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            theStore.Tenancy.Default.DbObjects.SchemaTables().Any(x => x.Name == "mt_fake_table")
                .ShouldBeTrue();
        }

        public void using_custom_feature_schema()
        {
            #region sample_adding-schema-feature
            var store = DocumentStore.For(_ =>
            {
                // Creates a new instance of FakeStorage and
                // passes along the current StoreOptions
                _.Storage.Add<FakeStorage>();

                // or

                _.Storage.Add(new FakeStorage(_));
            });
            #endregion sample_adding-schema-feature
        }

        public ability_to_add_custom_storage_features(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    #region sample_creating-a-fake-schema-feature
    public class FakeStorage : FeatureSchemaBase
    {
        public FakeStorage(StoreOptions options) : base("fake", options)
        {
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            var table = new Table(new DbObjectName(Options.DatabaseSchemaName, "mt_fake_table"));
            table.AddColumn("name", "varchar");

            yield return table;
        }
    }

    #endregion sample_creating-a-fake-schema-feature
}
