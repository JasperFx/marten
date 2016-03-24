using System.Diagnostics;
using System.Linq;
using Marten.Schema;
using Marten.Schema.Hierarchies;
using Marten.Schema.Sequences;
using Marten.Testing.Documents;
using Marten.Testing.Schema.Hierarchies;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class DocumentMappingTests
    {
        [Fact]
        public void default_alias_for_a_type_that_is_not_nested()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.Alias.ShouldBe("user");
        }

        [Fact]
        public void default_table_name()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.TableName.ShouldBe("mt_doc_user");
        }

        [Fact]
        public void default_table_name_with_schema()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.QualifiedTableName.ShouldBe("public.mt_doc_user");
        }

        [Fact]
        public void default_table_name_with_different_shema()
        {
            var mapping = DocumentMappingFactory.For<User>("other");
            mapping.QualifiedTableName.ShouldBe("other.mt_doc_user");
        }

        [Fact]
        public void default_upsert_name()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.UpsertName.ShouldBe("mt_upsert_user");
        }

        [Fact]
        public void default_upsert_name_with_schema()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.QualifiedUpsertName.ShouldBe("public.mt_upsert_user");
        }

        [Fact]
        public void default_upsert_name_with_different_schema()
        {
            var mapping = DocumentMappingFactory.For<User>("other");
            mapping.QualifiedUpsertName.ShouldBe("other.mt_upsert_user");
        }

        [Fact]
        public void override_the_alias()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.Alias = "users";

            mapping.TableName.ShouldBe("mt_doc_users");
            mapping.UpsertName.ShouldBe("mt_upsert_users");
        }

        [Fact]
        public void override_the_alias_converts_tablename_with_schema_to_lowercase()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.Alias = "Users";

            mapping.QualifiedTableName.ShouldBe("public.mt_doc_users");
        }

        [Fact]
        public void override_the_alias_converts_tablename_with_different_schema_to_lowercase()
        {
            var mapping = DocumentMappingFactory.For<User>("OTHER");
            mapping.Alias = "Users";

            mapping.QualifiedTableName.ShouldBe("other.mt_doc_users");
        }

        [Fact]
        public void override_the_alias_converts_upsertname_with_schema_to_lowercase()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.Alias = "Users";

            mapping.QualifiedUpsertName.ShouldBe("public.mt_upsert_users");
        }

        [Fact]
        public void override_the_alias_converts_upsertname_with_different_schema_to_lowercase()
        {
            var mapping = DocumentMappingFactory.For<User>("OTHER");
            mapping.Alias = "Users";

            mapping.QualifiedUpsertName.ShouldBe("other.mt_upsert_users");
        }

        [Fact]
        public void override_the_alias_converts_table_name_to_lowercase()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.Alias = "Users";

            mapping.TableName.ShouldBe("mt_doc_users");
        }

        [Fact]
        public void override_the_alias_converts_upsertname_to_lowercase()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.Alias = "Users";

            mapping.UpsertName.ShouldBe("mt_upsert_users");
        }

        [Fact]
        public void override_the_alias_converts_alias_to_lowercase()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.Alias = "Users";

            mapping.Alias.ShouldBe("users");
        }

        [Fact]
        public void select_fields_for_non_hierarchy_mapping()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.SelectFields().ShouldHaveTheSameElementsAs("data", "id");
        }

        [Fact]
        public void no_storage_arguments_with_simple_id()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.ToArguments().Any().ShouldBeFalse();
        }

        [Fact]
        public void storage_arguments_from_id_member()
        {
            var mapping = DocumentMappingFactory.For<IntDoc>();
            mapping.ToArguments().Single().ShouldBeOfType<HiloIdGeneration>();
        }

        [Fact]
        public void storage_arguments_adds_hierarchy_argument_with_subclasses()
        {
            var mapping = DocumentMappingFactory.For<Squad>();
            mapping.AddSubClass(typeof(FootballTeam));

            mapping.ToArguments().OfType<HierarchyArgument>()
                .Single().Mapping.ShouldBeSameAs(mapping);
        }

        [Fact]
        public void to_table_without_subclasses_and_no_duplicated_fields()
        {
            var mapping = DocumentMappingFactory.For<IntDoc>();
            mapping.ToTable(null).Columns.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("id", "data");
        }

        [Fact]
        public void to_table_columns_with_duplicated_fields()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.DuplicateField("FirstName");
            mapping.ToTable(null).Columns.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("id", "data", "first_name");

        }

        [Fact]
        public void to_table_columns_with_subclasses()
        {
            var mapping = DocumentMappingFactory.For<Squad>();
            mapping.AddSubClass(typeof(BaseballTeam));

            var table = mapping.ToTable(null);

            var typeColumn = table.Columns.Last();
            typeColumn.Name.ShouldBe(DocumentMapping.DocumentTypeColumn);
            typeColumn.Type.ShouldBe("varchar");
        }

        [Fact]
        public void to_upsert_baseline()
        {
            var mapping = DocumentMappingFactory.For<Squad>();
            var function = mapping.ToUpsertFunction();

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data");
        }

        [Fact]
        public void to_upsert_with_duplicated_fields()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.DuplicateField("FirstName");
            mapping.DuplicateField("LastName");

            var function = mapping.ToUpsertFunction();

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data", "first_name", "last_name");
        }

        [Fact]
        public void to_upsert_with_subclasses()
        {
            var mapping = DocumentMappingFactory.For<Squad>();
            mapping.AddSubClass(typeof(BaseballTeam));

            var function = mapping.ToUpsertFunction();

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data", DocumentMapping.DocumentTypeColumn);
        }

        [Fact]
        public void select_fields_without_subclasses()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.SelectFields().ShouldHaveTheSameElementsAs("data", "id");
        }

        [Fact]
        public void select_fields_with_subclasses()
        {
            var mapping = DocumentMappingFactory.For<Squad>();
            mapping.AddSubClass(typeof(BaseballTeam));

            mapping.SelectFields().ShouldHaveTheSameElementsAs("data", "id", DocumentMapping.DocumentTypeColumn);
        }

        [Fact]
        public void is_hierarchy__is_false_for_concrete_type_with_no_subclasses()
        {
            DocumentMappingFactory.For<User>().IsHierarchy().ShouldBeFalse();
        }

        [Fact]
        public void concrete_type_with_subclasses_is_hierarchy()
        {
            var mapping = DocumentMappingFactory.For<User>();
            mapping.AddSubClass(typeof(SuperUser));

            mapping.IsHierarchy().ShouldBeTrue();
        }

        [Fact]
        public void is_hierarchy_always_true_for_abstract_type()
        {
            DocumentMappingFactory.For<AbstractDoc>()
                .IsHierarchy().ShouldBeTrue();
        }

        [Fact]
        public void is_hierarchy_always_true_for_interface()
        {
            DocumentMappingFactory.For<IDoc>().IsHierarchy()
                .ShouldBeTrue();
        }

        public abstract class AbstractDoc
        {
            public int id;
        }

        public interface IDoc
        {
            string id { get; set; }
        }
    }
}