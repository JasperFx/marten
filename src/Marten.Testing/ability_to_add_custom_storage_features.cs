using Marten.Schema;
using Marten.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Marten.Testing
{
    public class ability_to_add_custom_storage_features : IntegratedFixture
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
    }

    public class FakeStorage : FeatureSchemaBase
    {
        public FakeStorage(StoreOptions options) : base("fake", options)
        {
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            var table = new Table(new DbObjectName(Options.DatabaseSchemaName,"mt_fake_table"));
            table.AddColumn("name", "varchar");

            yield return table;
        }
    }
}