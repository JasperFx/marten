using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_550_schema_diff_with_precision : IntegratedFixture
    {
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
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<DocWithPrecision>().Duplicate(x => x.Name, "character varying (100)");
            });

            var diff = store.Storage.MappingFor(typeof(DocWithPrecision))
                .SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store.Schema);

            diff.FunctionDiff.HasChanged.ShouldBeFalse();
            diff.TableDiff.Different.Any().ShouldBeFalse();

            diff.HasDifferences().ShouldBeFalse();
        }


    }

    public class DocWithPrecision
    {
        public Guid Id;


        public string Name { get; set; }
    }
}