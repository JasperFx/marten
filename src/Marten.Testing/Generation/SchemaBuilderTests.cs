using Marten.Generation;
using Shouldly;

namespace Marten.Testing.Generation
{
    public class SchemaBuilderTests
    {
        public void table_name_for_document()
        {
            SchemaBuilder.TableNameFor(typeof(MySpecialDocument))
                .ShouldBe("mt_doc_MySpecialDocument");
        }

        public void write_document_table()
        {
            var builder = new SchemaBuilder();
            builder.CreateTable(typeof(MySpecialDocument));

            var sql = builder.ToSql();

            sql.ShouldContain("CREATE TABLE mt_doc_MySpecialDocument");
            sql.ShouldContain("json NOT NULL");
        }

        public class MySpecialDocument { }
    }
}