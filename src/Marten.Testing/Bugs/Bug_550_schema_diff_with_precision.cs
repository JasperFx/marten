using System;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    [Collection("bug550")]
    public class Bug_550_schema_diff_with_precision: OneOffConfigurationsContext
    {
        public Bug_550_schema_diff_with_precision() : base("bug550")
        {
        }

        [Fact]
        public void can_handle_the_explicit_precision()
        {
            // Configure a doc
            StoreOptions(_ =>
            {
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = SchemaName;
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            var patch = store.Schema.ToPatch(typeof(DocWithPrecision));
            patch.Difference.ShouldBe(SchemaPatchDifference.None);
        }


    }

    public class DocWithPrecision
    {
        public Guid Id;

        public string Name { get; set; }
    }
}
