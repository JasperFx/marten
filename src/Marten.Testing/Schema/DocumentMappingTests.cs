using System;
using System.IO;
using System.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing.Schema
{
    public class DocumentMappingTests
    {
        public void default_table_name()
        {
            var mapping = new DocumentMapping(typeof (User));
            mapping.TableName.ShouldBe("mt_doc_user");
        }

        public void pick_up_upper_case_property_id()
        {
            var mapping = new DocumentMapping(typeof (UpperCaseProperty));
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(UpperCaseProperty.Id));
        }

        public void pick_up_lower_case_property_id()
        {
            var mapping = new DocumentMapping(typeof (LowerCaseProperty));
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(LowerCaseProperty.id));
        }

        public void pick_up_lower_case_field_id()
        {
            var mapping = new DocumentMapping(typeof (LowerCaseField));
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(LowerCaseField.id));
        }

        public void pick_up_upper_case_field_id()
        {
            var mapping = new DocumentMapping(typeof (UpperCaseField));
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(UpperCaseField.Id));
        }

        public void generate_simple_document_table()
        {
            var mapping = new DocumentMapping(typeof (MySpecialDocument));
            var builder = new StringWriter();

            mapping.WriteSchemaObjects(null, builder);

            var sql = builder.ToString();

            sql.ShouldContain("CREATE TABLE mt_doc_myspecialdocument");
            sql.ShouldContain("jsonb NOT NULL");
        }

        public void write_upsert_sql()
        {
            var mapping = new DocumentMapping(typeof (MySpecialDocument));
            var builder = new StringWriter();

            mapping.WriteSchemaObjects(null, builder);

            var sql = builder.ToString();

            sql.ShouldContain("INSERT INTO mt_doc_myspecialdocument");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_myspecialdocument");
        }

        public void table_name_for_document()
        {
            DocumentMapping.TableNameFor(typeof (MySpecialDocument))
                .ShouldBe("mt_doc_myspecialdocument");
        }

        public void upsert_name_for_document_type()
        {
            DocumentMapping.UpsertNameFor(typeof (MySpecialDocument))
                .ShouldBe("mt_upsert_myspecialdocument");
        }


        public class UpperCaseProperty
        {
            public Guid Id { get; set; }
        }

        public class LowerCaseProperty
        {
            public Guid id { get; set; }
        }

        public class UpperCaseField
        {
            public int Id;
        }

        public class LowerCaseField
        {
            public int id;
        }

        public class MySpecialDocument
        {
            public Guid Id { get; set; }
        }
    }
}