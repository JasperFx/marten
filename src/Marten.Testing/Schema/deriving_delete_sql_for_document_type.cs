using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class deriving_delete_sql_for_document_type
    {
        [Fact]
        public void delete_by_id_for_remove_style_deletion()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DeleteStyle = DeleteStyle.Remove;

            var resolver = new DocumentStorage<User>(mapping);

            resolver.DeleteByIdSql.ShouldBe("delete from public.mt_doc_user as d where id = ?");
        }

        [Fact]
        public void delete_by_id_for_soft_delete_style_deletion()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DeleteStyle = DeleteStyle.SoftDelete;

            var resolver = new DocumentStorage<User>(mapping);

            resolver.DeleteByIdSql.ShouldBe("update public.mt_doc_user as d set mt_deleted = True, mt_deleted_at = now() where id = ?");
        }

        [Fact]
        public void delete_by_where_for_remove_style_deletion()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DeleteStyle = DeleteStyle.Remove;

            var resolver = new DocumentStorage<User>(mapping);

            resolver.DeleteByWhereSql.ShouldBe("delete from public.mt_doc_user as d where ?");
        }

        [Fact]
        public void delete_by_where_for_soft_delete_style_deletion()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.DeleteStyle = DeleteStyle.SoftDelete;

            var resolver = new DocumentStorage<User>(mapping);

            resolver.DeleteByWhereSql.ShouldBe("update public.mt_doc_user as d set mt_deleted = True, mt_deleted_at = now() where ?");
        }
    }
}