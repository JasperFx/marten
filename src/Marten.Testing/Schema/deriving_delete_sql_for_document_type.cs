using Marten.Schema;
using Marten.Testing.Acceptance;
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

            resolver.DeleteByIdSql.ShouldBe("update public.mt_doc_user as d set mt_deleted = true, mt_deleted_at = now() where id = ?");
        }

        [Fact]
        public void delete_by_id_for_soft_delete_style_deletion_with_metadata_mapping()
        {

            var mapping = DocumentMapping.For<DocWithMeta>();
            mapping.DeleteStyle = DeleteStyle.SoftDelete;
            mapping.IsSoftDeletedMember = typeof(DocWithMeta).GetMember("Deleted")[0];

            var resolver = new DocumentStorage<DocWithMeta>(mapping);

            resolver.DeleteByIdSql.ShouldBe("update public.mt_doc_docwithmeta as d set mt_deleted = true, mt_deleted_at = now(), data = jsonb_set(data,'{Deleted}','\"true\"'::jsonb)::jsonb where id = ?");
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

            resolver.DeleteByWhereSql.ShouldBe("update public.mt_doc_user as d set mt_deleted = true, mt_deleted_at = now() where ?");
        }

        [Fact]
        public void delete_by_where_for_soft_delete_style_deletion_with_metadata_mappings()
        {

            var mapping = DocumentMapping.For<DocWithMeta>();
            mapping.DeleteStyle = DeleteStyle.SoftDelete;
            mapping.IsSoftDeletedMember = typeof(DocWithMeta).GetMember("Deleted")[0];
            mapping.SoftDeletedAtMember = typeof(DocWithMeta).GetMember("DeletedAt")[0];

            var resolver = new DocumentStorage<DocWithMeta>(mapping);
            resolver.DeleteByWhereSql.ShouldBe("update public.mt_doc_docwithmeta as d set mt_deleted = true, mt_deleted_at = now(), data = jsonb_set(jsonb_set(data,'{Deleted}','\"true\"'::jsonb)::jsonb,'{DeletedAt}', to_jsonb(now()))::jsonb where ?");
        }

    }
}
