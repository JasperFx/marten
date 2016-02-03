using Marten.Schema;
using Marten.Testing.Documents;
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
    }
}