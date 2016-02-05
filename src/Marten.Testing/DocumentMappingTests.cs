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
            var mapping = DocumentMapping.For<User>();
            mapping.Alias.ShouldBe("user");
        }

        [Fact]
        public void default_table_name()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.TableName.ShouldBe("mt_doc_user");
        }

        [Fact]
        public void default_upsert_name()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.UpsertName.ShouldBe("mt_upsert_user");
        }

        [Fact]
        public void override_the_alias()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.Alias = "users";

            mapping.TableName.ShouldBe("mt_doc_users");
            mapping.UpsertName.ShouldBe("mt_upsert_users");
        }

        [Fact]
        public void select_fields_for_non_hierarchy_mapping()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.SelectFields("d").ShouldBe("d.data, d.id");
        }

        [Fact]
        public void no_storage_arguments_with_simple_id()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.ToArguments().Any().ShouldBeFalse();
        }

        [Fact]
        public void storage_arguments_from_id_member()
        {
            var mapping = DocumentMapping.For<IntDoc>();
            mapping.ToArguments().Single().ShouldBeOfType<HiloIdGeneration>();
        }

        [Fact]
        public void storage_arguments_adds_hierarchy_argument_with_subclasses()
        {
            var mapping = new DocumentMapping(typeof(Squad), new StoreOptions());
            mapping.AddSubClass(typeof(FootballTeam));

            mapping.ToArguments().OfType<HierarchyArgument>()
                .Single().Mapping.ShouldBeSameAs(mapping);
        }

        [Fact]
        public void to_table_without_subclasses_and_no_duplicated_fields()
        {
            var mapping = DocumentMapping.For<IntDoc>();
            mapping.ToTable(null).Columns.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("id", "data");
        }

        [Fact]
        public void to_table_columns_with_duplicated_fields()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DuplicateField("FirstName");
            mapping.ToTable(null).Columns.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("id", "data", "first_name");

        }

        [Fact]
        public void to_table_columns_with_subclasses()
        {
            var mapping = DocumentMapping.For<Squad>();
            mapping.AddSubClass(typeof(BaseballTeam));

            var table = mapping.ToTable(null);

            var typeColumn = table.Columns.Last();
            typeColumn.Name.ShouldBe(DocumentMapping.DocumentTypeColumn);
            typeColumn.Type.ShouldBe("varchar");
        }

        [Fact]
        public void to_upsert_baseline()
        {
            var mapping = DocumentMapping.For<Squad>();
            var function = mapping.ToUpsertFunction();

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data");
        }

        [Fact]
        public void to_upsert_with_duplicated_fields()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DuplicateField("FirstName");
            mapping.DuplicateField("LastName");

            var function = mapping.ToUpsertFunction();

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data", "first_name", "last_name");
        }

        [Fact]
        public void to_upsert_with_subclasses()
        {
            var mapping = DocumentMapping.For<Squad>();
            mapping.AddSubClass(typeof(BaseballTeam));

            var function = mapping.ToUpsertFunction();

            function.Arguments.Select(x => x.Column)
                .ShouldHaveTheSameElementsAs("id", "data", DocumentMapping.DocumentTypeColumn);
        }

        [Fact]
        public void select_fields_without_subclasses()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.SelectFields("d").ShouldBe("d.data, d.id");
        }

        [Fact]
        public void select_fields_with_subclasses()
        {
            var mapping = DocumentMapping.For<Squad>();
            mapping.AddSubClass(typeof(BaseballTeam));

            mapping.SelectFields("d").ShouldBe($"d.data, d.id, d.{DocumentMapping.DocumentTypeColumn}");
        }

        [Fact]
        public void to_resolve_method_without_subclasses()
        {
            var mapping = DocumentMapping.For<User>();
            var code = mapping.ToResolveMethod("User");


            code.ShouldContain("return map.Get<User>(id, json);");
        }

        [Fact]
        public void to_resolve_method_with_subclasses()
        {
            var mapping = DocumentMapping.For<Squad>();
            mapping.AddSubClass(typeof(BaseballTeam));
            mapping.AddSubClass(typeof(FootballTeam));

            var code = mapping.ToResolveMethod("User");

            code.ShouldContain("var typeAlias = reader.GetString(1);");
            code.ShouldContain("return map.Get<User>(id, _hierarchy.TypeFor(typeAlias), json);");
        }
    }
}