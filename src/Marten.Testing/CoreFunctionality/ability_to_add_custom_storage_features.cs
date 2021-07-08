using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Storage;
using Weasel.Postgresql;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class ability_to_add_custom_storage_features : IntegrationContext
    {
        [Fact]
        public async Task can_register_a_custom_feature()
        {
            StoreOptions(_ =>
            {
                _.Storage.Add<FakeStorage>();
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            (await theStore.Tenancy.Default.SchemaTables()).Any(x => x.Name == "mt_fake_table")
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
        private readonly StoreOptions _options;

        public FakeStorage(StoreOptions options) : base("fake")
        {
            _options = options;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            var table = new Table(new DbObjectName(_options.DatabaseSchemaName, "mt_fake_table"));
            table.AddColumn("name", "varchar");

            yield return table;
        }
    }

    #endregion sample_creating-a-fake-schema-feature
}
