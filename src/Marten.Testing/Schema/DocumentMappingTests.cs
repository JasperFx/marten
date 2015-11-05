using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Schema
{
    public class DocumentMappingTests
    {
        public void default_table_name()
        {
            var mapping = new DocumentMapping(typeof (User));
            mapping.TableName.ShouldBe("mt_doc_user");
        }

        public void default_search_mode_is_jsonb_to_record()
        {
            var mapping = new DocumentMapping(typeof(User));
            mapping.PropertySearching.ShouldBe(PropertySearching.JSONB_To_Record);
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

        public void generate_a_document_table_with_duplicated_tables()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DuplicateField("FirstName");

            var table = mapping.ToTable(null);

            table.Columns.Any(x => x.Name == "first_name").ShouldBeTrue();
        }

        public void generate_a_table_to_the_database_with_duplicated_field()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                container.GetInstance<DocumentCleaner>().CompletelyRemove(typeof(User));

                var schema = container.GetInstance<IDocumentSchema>();

                var mapping = schema.MappingFor(typeof(User));
                mapping.DuplicateField("FirstName");

                var storage = schema.StorageFor(typeof (User));

                schema.DocumentTables().ShouldContain(mapping.TableName);
            }
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

        public void find_field_for_immediate_property_that_is_not_duplicated()
        {
            var mapping = DocumentMapping.For<UpperCaseProperty>();
            var field = mapping.FieldFor("Id").ShouldBeOfType<LateralJoinField>();
            field.Members.Single().ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe("Id");
        }

        public void find_field_for_immediate_field_that_is_not_duplicated()
        {
            var mapping = DocumentMapping.For<UpperCaseField>();
            var field = mapping.FieldFor("Id").ShouldBeOfType<LateralJoinField>();
            field.Members.Single().ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe("Id");
        }

        public void duplicate_a_field()
        {
            var mapping = DocumentMapping.For<User>();

            mapping.DuplicateField("FirstName");

            mapping.FieldFor("FirstName").ShouldBeOfType<DuplicatedField>();

            // other fields are still the same

            mapping.FieldFor("LastName").ShouldNotBeOfType<DuplicatedField>();
        }

        public void switch_to_only_using_json_locator_fields()
        {
            var mapping = DocumentMapping.For<User>();

            mapping.DuplicateField("FirstName");

            mapping.PropertySearching = PropertySearching.JSON_Locator_Only;

            mapping.FieldFor("LastName").ShouldBeOfType<JsonLocatorField>();

            // leave duplicates alone

            mapping.FieldFor("FirstName").ShouldBeOfType<DuplicatedField>();
        }

        public void switch_back_to_lateral_join_searching_changes_the_non_duplicated_fields()
        {
            var mapping = DocumentMapping.For<User>();

            mapping.DuplicateField("FirstName");

            mapping.PropertySearching = PropertySearching.JSON_Locator_Only;

            // put it back the way it was
            mapping.PropertySearching = PropertySearching.JSONB_To_Record;

            mapping.FieldFor("LastName").ShouldBeOfType<LateralJoinField>();

            // leave duplicates alone

            mapping.FieldFor("FirstName").ShouldBeOfType<DuplicatedField>();
        }

        public void picks_up_searchable_attribute_on_fields()
        {
            var mapping = DocumentMapping.For<Organization>();

            mapping.FieldFor("OtherName").ShouldBeOfType<DuplicatedField>();
            mapping.FieldFor(nameof(Organization.OtherField)).ShouldNotBeOfType<DuplicatedField>();
        }

        public void picks_up_searchable_attribute_on_properties()
        {
            var mapping = DocumentMapping.For<Organization>();

            mapping.FieldFor(nameof(Organization.Name)).ShouldBeOfType<DuplicatedField>();
            mapping.FieldFor(nameof(Organization.OtherProp)).ShouldNotBeOfType<DuplicatedField>();
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

        public class Organization
        {
            public Guid Id { get; set; }

            [Searchable]
            public string Name { get; set; }

            [Searchable]
            public string OtherName;

            public string OtherProp;
            public string OtherField { get; set; }
        }
    }
}