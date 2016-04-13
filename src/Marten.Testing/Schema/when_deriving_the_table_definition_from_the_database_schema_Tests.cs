using System.Linq;
using Baseline;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    
    public class when_deriving_the_table_definition_from_the_database_schema_Tests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;
        private DocumentMapping theMapping;
        private IDocumentStorage _storage;
        private TableDefinition theDerivedTable;

        public when_deriving_the_table_definition_from_the_database_schema_Tests()
        {
            _schema = theStore.Schema;

            theMapping = _schema.MappingFor(typeof(User)).As<DocumentMapping>();
            theMapping.DuplicateField("UserName");


            _storage = _schema.StorageFor(typeof(User));

            theDerivedTable = _schema.TableSchema(theMapping);
        }

        [Fact]
        public void it_maps_the_table_name()
        {
            theDerivedTable.Table.ShouldBe(theMapping.Table);
        }

        [Fact]
        public void it_finds_the_primary_key()
        {
            theDerivedTable.PrimaryKey.ShouldNotBeNull();
            theDerivedTable.PrimaryKey.Name.ShouldBe("id");
        }

        [Fact]
        public void it_has_all_the_columns()
        {
            theDerivedTable.Columns.Select(x => x.Name).ShouldHaveTheSameElementsAs("id", "data", "user_name");
        }

        [Fact]
        public void it_can_map_the_database_type()
        {
            theDerivedTable.PrimaryKey.Type.ShouldBe("uuid");
            theDerivedTable.Column("data").Type.ShouldBe("jsonb");
            theDerivedTable.Column("user_name").Type.ShouldBe("character varying");
        }

    }
}