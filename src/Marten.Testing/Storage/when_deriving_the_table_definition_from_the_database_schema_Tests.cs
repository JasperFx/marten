using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Storage
{
    
    public class when_deriving_the_table_definition_from_the_database_schema_Tests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;
        private DocumentMapping theMapping;
        private IDocumentStorage _storage;
        private DocumentTable theDerivedTable;

        public when_deriving_the_table_definition_from_the_database_schema_Tests()
        {
            _schema = theStore.Schema;

            theMapping = theStore.Tenancy.Default.MappingFor(typeof(User)).As<DocumentMapping>();
            theMapping.DuplicateField("UserName");


            _storage = theStore.Tenancy.Default.StorageFor(typeof(User));

            theDerivedTable = new DocumentTable(theMapping);
        }

        [Fact]
        public void it_maps_the_table_name()
        {
            theDerivedTable.Identifier.ShouldBe(theMapping.Table);
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
            theDerivedTable.Select(x => x.Name).ShouldHaveTheSameElementsAs("id", "data", DocumentMapping.LastModifiedColumn, DocumentMapping.VersionColumn, DocumentMapping.DotNetTypeColumn, "user_name");
        }

        [Fact]
        public void it_can_map_the_database_type()
        {
            theDerivedTable.PrimaryKey.Type.ShouldBe("uuid");
            theDerivedTable.Column("data").Type.ShouldBe("jsonb");
            theDerivedTable.Column("user_name").Type.ShouldBe("varchar");
        }

    }
}