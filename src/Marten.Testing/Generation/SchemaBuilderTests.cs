using System;
using Marten.Generation;
using Shouldly;

namespace Marten.Testing.Generation
{
    public class SchemaBuilderTests
    {
        public void table_name_for_document()
        {
            SchemaBuilder.TableNameFor(typeof(MySpecialDocument))
                .ShouldBe("mt_doc_myspecialdocument");
        }

        public void write_document_table()
        {
            var builder = new SchemaBuilder();
            builder.CreateTable(typeof(MySpecialDocument), typeof(Guid));

            var sql = builder.ToSql();

            sql.ShouldContain("CREATE TABLE mt_doc_myspecialdocument");
            sql.ShouldContain("jsonb NOT NULL");
        }

        public void upsert_name_for_document_type()
        {
            SchemaBuilder.UpsertNameFor(typeof(MySpecialDocument))
                .ShouldBe("mt_upsert_myspecialdocument");
        }

        public void write_upsert_sql()
        {
            var builder = new SchemaBuilder();
            builder.DefineUpsert(typeof(MySpecialDocument), typeof(Guid));

            var sql = builder.ToSql();

            sql.ShouldContain("INSERT INTO mt_doc_myspecialdocument");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_myspecialdocument");
        }

        public class MySpecialDocument { }
    }
}