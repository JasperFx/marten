using System;
using Marten.Generation;
using Marten.Schema;
using Shouldly;

namespace Marten.Testing.Generation
{
    public class SchemaBuilderTests
    {
        public void table_name_for_document()
        {
            DocumentMapping.TableNameFor(typeof(MySpecialDocument))
                .ShouldBe("mt_doc_myspecialdocument");
        }

        public void upsert_name_for_document_type()
        {
            DocumentMapping.UpsertNameFor(typeof(MySpecialDocument))
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

        public class MySpecialDocument
        {
            public Guid Id { get; set; }
        }
    }
}