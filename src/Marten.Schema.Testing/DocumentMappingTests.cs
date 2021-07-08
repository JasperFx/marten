using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Linq.Fields;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Testing.Documents;
using Marten.Schema.Testing.Hierarchies;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Testing.Harness;
using NpgsqlTypes;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Schema.Testing
{
    public class DocumentMappingTests: IntegrationContext
    {
        public class FieldId
        {
            public string id;
        }

        public abstract class AbstractDoc
        {
            public int id;
        }

        public interface IDoc
        {
            string id { get; set; }
        }

        [UseOptimisticConcurrency]
        public class VersionedDoc
        {
            public Guid Id { get; set; } = Guid.NewGuid();
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

        [GinIndexed]
        public class BaseDocumentWithAttribute
        {
            public int Id;
        }

        public class BaseDocumentSubClass : BaseDocumentWithAttribute
        {
        }

        [PropertySearching(PropertySearching.JSON_Locator_Only)]
        public class Organization
        {
            [DuplicateField] public string OtherName;

            public string OtherProp;
            public Guid Id { get; set; }

            [DuplicateField]
            public string Name { get; set; }

            public string OtherField { get; set; }
        }

        public class CustomIdGeneration: IIdGeneration
        {
            public IEnumerable<Type> KeyTypes { get; }

            public bool RequiresSequences { get; } = false;
            public void GenerateCode(GeneratedMethod assign, DocumentMapping mapping)
            {
                throw new NotSupportedException();
            }
        }

        #region sample_ConfigureMarten-generic
        public class ConfiguresItself
        {
            public Guid Id;

            public static void ConfigureMarten(DocumentMapping mapping)
            {
                mapping.Alias = "different";
            }
        }

        #endregion sample_ConfigureMarten-generic

        #region sample_ConfigureMarten-specifically
        public class ConfiguresItselfSpecifically
        {
            public Guid Id;
            public string Name;

            public static void ConfigureMarten(DocumentMapping<ConfiguresItselfSpecifically> mapping)
            {
                mapping.Duplicate(x => x.Name);
            }
        }

        #endregion sample_ConfigureMarten-specifically

        [Fact]
        public void can_replace_hilo_def_settings()
        {
            var mapping = DocumentMapping.For<LongId>();

            var newDef = new HiloSettings { MaxLo = 33 };

            mapping.HiloSettings = newDef;

            var sequence = mapping.IdStrategy.ShouldBeOfType<HiloIdGeneration>();
            sequence.MaxLo.ShouldBe(newDef.MaxLo);
        }

        [Fact]
        public void concrete_type_with_subclasses_is_hierarchy()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.SubClasses.Add(typeof(SuperUser));

            mapping.IsHierarchy().ShouldBeTrue();
        }

        [Fact]
        public void default_alias_for_a_type_that_is_not_nested()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias.ShouldBe("user");
        }

        [Fact]
        public void default_search_mode_is_jsonb_to_record()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.PropertySearching.ShouldBe(PropertySearching.JSON_Locator_Only);
        }

        [Fact]
        public void default_table_name()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.TableName.Name.ShouldBe("mt_doc_user");
        }

        [Fact]
        public void default_table_name_2()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.TableName.QualifiedName.ShouldBe("public.mt_doc_user");
        }

        [Fact]
        public void default_table_name_on_other_schema()
        {
            var mapping = DocumentMapping.For<User>("other");
            mapping.TableName.QualifiedName.ShouldBe("other.mt_doc_user");
        }

        [Fact]
        public void default_table_name_on_overriden_schema()
        {
            var mapping = DocumentMapping.For<User>("other");
            mapping.DatabaseSchemaName = "overriden";
            mapping.TableName.QualifiedName.ShouldBe("overriden.mt_doc_user");
        }

        [Fact]
        public void default_table_name_with_different_shema()
        {
            var mapping = DocumentMapping.For<User>("other");
            mapping.TableName.QualifiedName.ShouldBe("other.mt_doc_user");
        }

        [Fact]
        public void default_table_name_with_schema()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.TableName.QualifiedName.ShouldBe("public.mt_doc_user");
        }

        [Fact]
        public void default_upsert_name()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.UpsertFunction.Name.ShouldBe("mt_upsert_user");
        }

        [Fact]
        public void default_upsert_name_with_different_schema()
        {
            var mapping = DocumentMapping.For<User>("other");
            mapping.UpsertFunction.QualifiedName.ShouldBe("other.mt_upsert_user");
        }

        [Fact]
        public void default_upsert_name_with_schema()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.UpsertFunction.QualifiedName.ShouldBe("public.mt_upsert_user");
        }

        [Theory]
        [InlineData(EnumStorage.AsInteger)]
        [InlineData(EnumStorage.AsString)]
        public void enum_storage_should_be_taken_from_store_options(EnumStorage enumStorage)
        {
            var storeOptions = new StoreOptions();
            storeOptions.UseDefaultSerialization(enumStorage);

            var mapping = new DocumentMapping<User>(storeOptions);
            mapping.EnumStorage.ShouldBe(enumStorage);
        }

        [Fact]
        public void doc_type_with_use_optimistic_concurrency_attribute()
        {
            DocumentMapping.For<VersionedDoc>()
                .UseOptimisticConcurrency.ShouldBeTrue();
        }

        [Fact]
        public void duplicate_a_field()
        {
            var mapping = DocumentMapping.For<User>();

            mapping.DuplicateField(nameof(User.FirstName));

            mapping.FieldFor(nameof(User.FirstName)).ShouldBeOfType<DuplicatedField>();

            // other fields are still the same

            SpecificationExtensions.ShouldNotBeOfType<DuplicatedField>(mapping.FieldFor(nameof(User.LastName)));
        }

        [Theory]
        [InlineData(EnumStorage.AsInteger, NpgsqlDbType.Integer)]
        [InlineData(EnumStorage.AsString, NpgsqlDbType.Varchar)]
        public void duplicated_field_enum_storage_should_be_taken_from_store_options_enum_storage_by_default(EnumStorage enumStorage, NpgsqlDbType expectedNpgsqlDbType)
        {
            var storeOptions = new StoreOptions();
            storeOptions.UseDefaultSerialization(enumStorage);

            var mapping = new DocumentMapping<Target>(storeOptions);

            var duplicatedField = mapping.DuplicateField(nameof(Target.Color));
            duplicatedField.DbType.ShouldBe(expectedNpgsqlDbType);
        }

        [Theory]
        [InlineData(EnumStorage.AsInteger, NpgsqlDbType.Integer)]
        [InlineData(EnumStorage.AsString, NpgsqlDbType.Varchar)]
        public void duplicated_field_enum_storage_should_be_taken_from_store_options_duplicated_field_enum_storage_when_it_was_changed(EnumStorage enumStorage, NpgsqlDbType expectedNpgsqlDbType)
        {
            var storeOptions = new StoreOptions();
            storeOptions.Advanced.DuplicatedFieldEnumStorage = enumStorage;

            var mapping = new DocumentMapping<Target>(storeOptions);

            var duplicatedField = mapping.DuplicateField(nameof(Target.Color));
            duplicatedField.DbType.ShouldBe(expectedNpgsqlDbType);
        }

        [Theory]
        [InlineData(true, NpgsqlDbType.Timestamp)]
        [InlineData(false, NpgsqlDbType.TimestampTz)]
        public void duplicated_field_date_time_db_type_should_be_taken_from_store_options_useTimestampWithoutTimeZoneForDateTime(bool useTimestampWithoutTimeZoneForDateTime, NpgsqlDbType expectedNpgsqlDbType)
        {
            var storeOptions = new StoreOptions();
            storeOptions.Advanced.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime = useTimestampWithoutTimeZoneForDateTime;

            var mapping = new DocumentMapping<Target>(storeOptions);

            var duplicatedField = mapping.DuplicateField(nameof(Target.Date));
            duplicatedField.DbType.ShouldBe(expectedNpgsqlDbType);
        }

        [Fact]
        public void find_field_for_immediate_field_that_is_not_duplicated()
        {
            var mapping = DocumentMapping.For<UpperCaseField>();
            var field = mapping.FieldFor("Id");
            field.Members.Single().ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe("Id");
        }

        [Fact]
        public void find_field_for_immediate_property_that_is_not_duplicated()
        {
            var mapping = DocumentMapping.For<UpperCaseProperty>();
            var field = mapping.FieldFor("Id");
            field.Members.Single().ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe("Id");
        }

        [Fact]
        public async Task generate_a_table_to_the_database_with_duplicated_field()
        {
            using var store = DocumentStore.For(ConnectionSource.ConnectionString);
            store.Advanced.Clean.CompletelyRemove(typeof(User));

            var mapping = store.Storage.MappingFor(typeof(User)).As<DocumentMapping>();
            mapping.DuplicateField(nameof(User.FirstName));

            store.Tenancy.Default.EnsureStorageExists(typeof(User));

            (await store.Tenancy.Default.DocumentTables()).Select(x => x.QualifiedName).ShouldContain(mapping.TableName.QualifiedName);
        }

        [Fact]
        public void get_the_sql_locator_for_the_Id_member()
        {
            DocumentMapping.For<User>().FieldFor("Id")
                .TypedLocator.ShouldBe("d.id");

            DocumentMapping.For<FieldId>().FieldFor("id")
                .TypedLocator.ShouldBe("d.id");
        }

        [Fact]
        public void is_hierarchy__is_false_for_concrete_type_with_no_subclasses()
        {
            DocumentMapping.For<User>().IsHierarchy().ShouldBeFalse();
        }

        [Fact]
        public void is_hierarchy_always_true_for_abstract_type()
        {
            DocumentMapping.For<AbstractDoc>()
                .IsHierarchy().ShouldBeTrue();
        }

        [Fact]
        public void is_hierarchy_always_true_for_interface()
        {
            DocumentMapping.For<IDoc>().IsHierarchy()
                .ShouldBeTrue();
        }

        [Fact]
        public void optimistic_versioning_is_turned_off_by_default()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.UseOptimisticConcurrency.ShouldBeFalse();
        }

        [Fact]
        public void override_the_alias()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias = "users";

            mapping.TableName.Name.ShouldBe("mt_doc_users");
            mapping.UpsertFunction.Name.ShouldBe("mt_upsert_users");
        }

        [Fact]
        public void override_the_alias_converts_alias_to_lowercase()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias = "Users";

            mapping.Alias.ShouldBe("users");
        }

        [Fact]
        public void override_the_alias_converts_table_name_to_lowercase()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias = "Users";

            mapping.TableName.Name.ShouldBe("mt_doc_users");
        }

        [Fact]
        public void override_the_alias_converts_tablename_with_different_schema_to_lowercase()
        {
            var mapping = DocumentMapping.For<User>("OTHER");
            mapping.Alias = "Users";

            mapping.TableName.QualifiedName.ShouldBe("other.mt_doc_users");
        }

        [Fact]
        public void override_the_alias_converts_tablename_with_schema_to_lowercase()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias = "Users";

            mapping.TableName.QualifiedName.ShouldBe("public.mt_doc_users");
        }

        [Fact]
        public void override_the_alias_converts_upsertname_to_lowercase()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias = "Users";

            mapping.UpsertFunction.Name.ShouldBe("mt_upsert_users");
        }

        [Fact]
        public void override_the_alias_converts_upsertname_with_different_schema_to_lowercase()
        {
            var mapping = DocumentMapping.For<User>("OTHER");
            mapping.Alias = "Users";

            mapping.UpsertFunction.QualifiedName.ShouldBe("other.mt_upsert_users");
        }

        [Fact]
        public void override_the_alias_converts_upsertname_with_schema_to_lowercase()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias = "Users";

            mapping.UpsertFunction.QualifiedName.ShouldBe("public.mt_upsert_users");
        }

        [Fact]
        public void pick_up_lower_case_field_id()
        {
            var mapping = DocumentMapping.For<LowerCaseField>();
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(LowerCaseField.id));
        }

        [Fact]
        public void pick_up_lower_case_property_id()
        {
            var mapping = DocumentMapping.For<LowerCaseProperty>();
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(LowerCaseProperty.id));
        }

        [Fact]
        public void pick_up_upper_case_field_id()
        {
            var mapping = DocumentMapping.For<UpperCaseField>();
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(UpperCaseField.Id));
        }

        [Fact]
        public void pick_up_upper_case_property_id()
        {
            var mapping = DocumentMapping.For<UpperCaseProperty>();
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(UpperCaseProperty.Id));
        }

        [Fact]
        public void picks_up_marten_attribute_on_document_type()
        {
            var mapping = DocumentMapping.For<Organization>();
            mapping.PropertySearching.ShouldBe(PropertySearching.JSON_Locator_Only);
        }

        [Fact]
        public void picks_up_marten_ginindexed_attribute_on_document_type()
        {
            var mapping = DocumentMapping.For<BaseDocumentWithAttribute>();
            var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == "data");
            indexDefinition.Method.ShouldBe(IndexMethod.gin);

            var mappingSub = DocumentMapping.For<BaseDocumentSubClass>();
            indexDefinition = mappingSub.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == "data");
            indexDefinition.Method.ShouldBe(IndexMethod.gin);
        }

        [Fact]
        public void picks_up_searchable_attribute_on_fields()
        {
            var mapping = DocumentMapping.For<Organization>();

            mapping.FieldFor("OtherName").ShouldBeOfType<DuplicatedField>();
            SpecificationExtensions.ShouldNotBeOfType<DuplicatedField>(mapping.FieldFor(nameof(Organization.OtherField)));
        }

        [Fact]
        public void picks_up_searchable_attribute_on_properties()
        {
            var mapping = DocumentMapping.For<Organization>();

            mapping.FieldFor(nameof(Organization.Name)).ShouldBeOfType<DuplicatedField>();
            mapping.FieldFor(nameof(Organization.OtherProp)).ShouldNotBeOfType<DuplicatedField>();
        }

        [Fact]
        public void table_name_for_document()
        {
            DocumentMapping.For<MySpecialDocument>().TableName.Name
                .ShouldBe("mt_doc_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void table_name_with_schema_for_document()
        {
            DocumentMapping.For<MySpecialDocument>().TableName.QualifiedName
                .ShouldBe("public.mt_doc_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void table_name_with_schema_for_document_on_other_schema()
        {
            DocumentMapping.For<MySpecialDocument>("other").TableName.QualifiedName
                .ShouldBe("other.mt_doc_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void table_name_with_schema_for_document_on_overriden_schema()
        {
            var documentMapping = DocumentMapping.For<MySpecialDocument>("other");
            documentMapping.DatabaseSchemaName = "overriden";

            documentMapping.TableName.QualifiedName
                .ShouldBe("overriden.mt_doc_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void to_table_columns_with_duplicated_fields()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DuplicateField(nameof(User.FirstName));

            var table = new DocumentTable(mapping);

            table.Columns.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("id", "data", SchemaConstants.LastModifiedColumn,
                    SchemaConstants.VersionColumn, SchemaConstants.DotNetTypeColumn, "first_name");
        }

        [Fact]
        public void to_table_columns_with_subclasses()
        {
            var mapping = DocumentMapping.For<Squad>();
            mapping.SubClasses.Add(typeof(BaseballTeam));

            var table = new DocumentTable(mapping);

            var typeColumn = table.Columns.Last();
            typeColumn.Name.ShouldBe(SchemaConstants.DocumentTypeColumn);
            typeColumn.Type.ShouldBe("varchar");
        }

        [Fact]
        public void to_table_without_subclasses_and_no_duplicated_fields()
        {
            var mapping = DocumentMapping.For<IntDoc>();
            var table = new DocumentTable(mapping);
            table.Columns.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("id", "data", SchemaConstants.LastModifiedColumn,
                    SchemaConstants.VersionColumn, SchemaConstants.DotNetTypeColumn);
        }

        [Fact]
        public void to_upsert_baseline()
        {
            var mapping = DocumentMapping.For<Squad>();
            var function = new UpsertFunction(mapping);

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data", SchemaConstants.VersionColumn,
                    SchemaConstants.DotNetTypeColumn);
        }

        [Fact]
        public void to_upsert_with_duplicated_fields()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DuplicateField(nameof(User.FirstName));
            mapping.DuplicateField(nameof(User.LastName));

            var function = new UpsertFunction(mapping);

            var args = function.Arguments.Select(x => x.Column).ToArray();
            args.ShouldContain("first_name");
            args.ShouldContain("last_name");
        }

        [Fact]
        public void to_upsert_with_subclasses()
        {
            var mapping = DocumentMapping.For<Squad>();
            mapping.SubClasses.Add(typeof(BaseballTeam));

            var function = new UpsertFunction(mapping);

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data", SchemaConstants.VersionColumn,
                    SchemaConstants.DotNetTypeColumn, SchemaConstants.DocumentTypeColumn);
        }

        [Fact]
        public void trying_to_replace_the_hilo_settings_when_not_using_hilo_for_the_sequence_throws()
        {
            Exception<InvalidOperationException>.ShouldBeThrownBy(
                () => { DocumentMapping.For<StringId>().HiloSettings = new HiloSettings(); });
        }

        [Fact]
        public void upsert_name_for_document_type()
        {
            DocumentMapping.For<MySpecialDocument>().UpsertFunction.Name
                .ShouldBe("mt_upsert_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void upsert_name_with_schema_for_document_type()
        {
            DocumentMapping.For<MySpecialDocument>().UpsertFunction.QualifiedName
                .ShouldBe("public.mt_upsert_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void upsert_name_with_schema_for_document_type_on_other_schema()
        {
            DocumentMapping.For<MySpecialDocument>("other").UpsertFunction.QualifiedName
                .ShouldBe("other.mt_upsert_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void upsert_name_with_schema_for_document_type_on_overriden_schema()
        {
            var documentMapping = DocumentMapping.For<MySpecialDocument>("other");
            documentMapping.DatabaseSchemaName = "overriden";

            documentMapping.UpsertFunction.QualifiedName
                .ShouldBe("overriden.mt_upsert_documentmappingtests_myspecialdocument");
        }

        [Fact]
        public void use_custom_id_generation_on_mapping_shoudl_be_settable()
        {
            var mapping = DocumentMapping.For<LongId>();

            mapping.IdStrategy = new CustomIdGeneration();
            mapping.IdStrategy.ShouldBeOfType<CustomIdGeneration>();
        }

        [Fact]
        public void use_guid_id_generation_for_guid_id()
        {
            var mapping = DocumentMapping.For<UpperCaseProperty>();
            mapping.IdStrategy.ShouldBeOfType<CombGuidIdGeneration>();
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
        public void use_string_id_generation_for_string()
        {
            var mapping = DocumentMapping.For<StringId>();
            mapping.IdStrategy.ShouldBeOfType<StringIdGeneration>();
        }

        [Fact]
        public void uses_ConfigureMarten_method_to_alter_mapping_upon_construction()
        {
            var mapping = DocumentMapping.For<ConfiguresItself>();
            mapping.Alias.ShouldBe("different");
        }

        [Fact]
        public void uses_ConfigureMarten_method_to_alter_mapping_upon_construction_with_the_generic_signature()
        {
            var mapping = DocumentMapping.For<ConfiguresItselfSpecifically>();
            mapping.DuplicatedFields.Single().MemberName.ShouldBe(nameof(ConfiguresItselfSpecifically.Name));
        }

        [Fact]
        public void trying_to_index_deleted_at_when_not_soft_deleted_document_throws()
        {
            Exception<InvalidOperationException>.ShouldBeThrownBy(() => DocumentMapping.For<IntId>().AddDeletedAtIndex());
        }

        [Fact]
        public void no_tenant_id_column_when_not_conjoined_tenancy()
        {
            var mapping = DocumentMapping.For<ConfiguresItselfSpecifically>();
            var table = new DocumentTable(mapping);

            table.HasColumn(TenantIdColumn.Name).ShouldBeFalse();
        }

        [Fact]
        public void add_the_tenant_id_column_when_it_is_conjoined_tenancy()
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Policies.AllDocumentsAreMultiTenanted();

            var mapping = new DocumentMapping(typeof(User), options);
            mapping.TenancyStyle = TenancyStyle.Conjoined;

            var table = new DocumentTable(mapping);
            table.Columns.Any(x => x is TenantIdColumn).ShouldBeTrue();
        }

        [Fact]
        public void no_overwrite_function_if_no_optimistic_concurrency()
        {
            var mapping = DocumentMapping.For<User>();
            var objects = mapping.Schema.Objects;

            objects.Length.ShouldBe(4);
            objects.Single(x => x.GetType() == typeof(UpsertFunction)).Identifier.ShouldBe(mapping.UpsertFunction);
        }

        [Fact]
        public void add_overwrite_function_if_optimistic_concurrency()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.UseOptimisticConcurrency = true;

            var objects = mapping.Schema.Objects;

            objects.Length.ShouldBe(5);
            objects.OfType<OverwriteFunction>().Any().ShouldBeTrue();
        }

        [Fact]
        public void default_metadata_columns()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Metadata.LastModified.Enabled.ShouldBeTrue();
            mapping.Metadata.Version.Enabled.ShouldBeTrue();
            mapping.Metadata.DotNetType.Enabled.ShouldBeTrue();
        }
    }
}
