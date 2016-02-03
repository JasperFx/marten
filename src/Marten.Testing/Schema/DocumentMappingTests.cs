using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Marten.Schema;
using Marten.Schema.Sequences;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentMappingTests
    {
        [Fact]
        public void default_table_name()
        {
            var mapping = new DocumentMapping(typeof (User));
            mapping.TableName.ShouldBe("mt_doc_user");
        }

        [Fact]
        public void default_search_mode_is_jsonb_to_record()
        {
            var mapping = new DocumentMapping(typeof(User));
            mapping.PropertySearching.ShouldBe(PropertySearching.JSON_Locator_Only);
        }

        [Fact]
        public void pick_up_upper_case_property_id()
        {
            var mapping = new DocumentMapping(typeof (UpperCaseProperty));
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(UpperCaseProperty.Id));
        }

        [Fact]
        public void pick_up_lower_case_property_id()
        {
            var mapping = new DocumentMapping(typeof (LowerCaseProperty));
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(LowerCaseProperty.id));
        }

        [Fact]
        public void pick_up_lower_case_field_id()
        {
            var mapping = new DocumentMapping(typeof (LowerCaseField));
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(LowerCaseField.id));
        }

        [Fact]
        public void pick_up_upper_case_field_id()
        {
            var mapping = new DocumentMapping(typeof (UpperCaseField));
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(UpperCaseField.Id));
        }

        [Fact]
        public void generate_simple_document_table()
        {
            var mapping = new DocumentMapping(typeof (MySpecialDocument));
            var builder = new StringWriter();

            SchemaBuilder.WriteSchemaObjects(mapping, null, builder);

            var sql = builder.ToString();

            sql.ShouldContain("CREATE TABLE mt_doc_documentmappingtests_myspecialdocument");
            sql.ShouldContain("jsonb NOT NULL");
        }

        [Fact]
        public void generate_table_with_indexes()
        {
            var mapping = new DocumentMapping(typeof(User));
            var i1 = mapping.AddIndex("first_name");
            var i2 = mapping.AddIndex("last_name");

            var builder = new StringWriter();

            SchemaBuilder.WriteSchemaObjects(mapping, null, builder);

            var sql = builder.ToString();

            

            sql.ShouldContain(i1.ToDDL());
            sql.ShouldContain(i2.ToDDL());

        }

        [Fact]
        public void generate_a_document_table_with_duplicated_tables()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DuplicateField("FirstName");

            var table = mapping.ToTable(null);

            table.Columns.Any(x => x.Name == "first_name").ShouldBeTrue();
        }

        [Fact]
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


        [Fact]
        public void write_upsert_sql()
        {
            var mapping = new DocumentMapping(typeof (MySpecialDocument));
            var builder = new StringWriter();

            SchemaBuilder.WriteSchemaObjects(mapping, null, builder);

            var sql = builder.ToString();

            sql.ShouldContain("INSERT INTO mt_doc_documentmappingtests_myspecialdocument");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void table_name_for_document()
        {
            new DocumentMapping(typeof(MySpecialDocument)).TableName
                .ShouldBe("mt_doc_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void upsert_name_for_document_type()
        {
            new DocumentMapping(typeof(MySpecialDocument)).UpsertName
                .ShouldBe("mt_upsert_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void find_field_for_immediate_property_that_is_not_duplicated()
        {
            var mapping = DocumentMapping.For<UpperCaseProperty>();
            var field = mapping.FieldFor("Id").ShouldBeOfType<LateralJoinField>();
            field.Members.Single().ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe("Id");
        }

        [Fact]
        public void find_field_for_immediate_field_that_is_not_duplicated()
        {
            var mapping = DocumentMapping.For<UpperCaseField>();
            var field = mapping.FieldFor("Id").ShouldBeOfType<LateralJoinField>();
            field.Members.Single().ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe("Id");
        }

        [Fact]
        public void duplicate_a_field()
        {
            var mapping = DocumentMapping.For<User>();

            mapping.DuplicateField("FirstName");

            mapping.FieldFor("FirstName").ShouldBeOfType<DuplicatedField>();

            // other fields are still the same

            mapping.FieldFor("LastName").ShouldNotBeOfType<DuplicatedField>();
        }

        [Fact]
        public void switch_to_only_using_json_locator_fields()
        {
            var mapping = DocumentMapping.For<User>();

            mapping.DuplicateField("FirstName");

            mapping.PropertySearching = PropertySearching.JSON_Locator_Only;

            mapping.FieldFor("LastName").ShouldBeOfType<JsonLocatorField>();

            // leave duplicates alone

            mapping.FieldFor("FirstName").ShouldBeOfType<DuplicatedField>();
        }

        [Fact]
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

        [Fact]
        public void picks_up_searchable_attribute_on_fields()
        {
            var mapping = DocumentMapping.For<Organization>();

            mapping.FieldFor("OtherName").ShouldBeOfType<DuplicatedField>();
            mapping.FieldFor(nameof(Organization.OtherField)).ShouldNotBeOfType<DuplicatedField>();
        }

        [Fact]
        public void picks_up_searchable_attribute_on_properties()
        {
            var mapping = DocumentMapping.For<Organization>();

            mapping.FieldFor(nameof(Organization.Name)).ShouldBeOfType<DuplicatedField>();
            mapping.FieldFor(nameof(Organization.OtherProp)).ShouldNotBeOfType<DuplicatedField>();
        }

        [Fact]
        public void picks_up_marten_attibute_on_document_type()
        {
            var mapping = DocumentMapping.For<Organization>();
            mapping.PropertySearching.ShouldBe(PropertySearching.JSON_Locator_Only);
        }

        [Fact]
        public void use_string_id_generation_for_string()
        {
            var mapping = DocumentMapping.For<StringId>();
            mapping.IdStrategy.ShouldBeOfType<StringIdGeneration>();
        }

        [Fact]
        public void use_guid_id_generation_for_guid_id()
        {
            var mapping = DocumentMapping.For<UpperCaseProperty>();
            mapping.IdStrategy.ShouldBeOfType<GuidIdGeneration>();
        }

        [Fact]
        public void use_hilo_id_generation_for_int_id()
        {
            DocumentMapping.For<IntId>()
                .IdStrategy.ShouldBeOfType<HiloIdGeneration>();
        }

        [Fact]
        public void use_hilo_id_generation_for_long_id()
        {
            DocumentMapping.For<LongId>()
                .IdStrategy.ShouldBeOfType<HiloIdGeneration>();
        }

        [Fact]
        public void can_replace_hilo_def_settings()
        {
            var mapping = DocumentMapping.For<LongId>();

            var newDef = new HiloSettings {Increment = 3, MaxLo = 33};

            mapping.HiloSettings(newDef);

            var sequence = mapping.IdStrategy.ShouldBeOfType<HiloIdGeneration>();
            sequence.MaxLo.ShouldBe(newDef.MaxLo);
            sequence.Increment.ShouldBe(newDef.Increment);
            
        }

        [Fact]
        public void trying_to_replace_the_hilo_settings_when_not_using_hilo_for_the_sequence_throws()
        {
            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                DocumentMapping.For<StringId>().HiloSettings(new HiloSettings());
            });
        }


        public class IntId
        {
            public int Id;
        }

        public class LongId
        {
            public long Id;
        }

        public class StringId
        {
            public string Id;
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

        [PropertySearching(PropertySearching.JSON_Locator_Only)]
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