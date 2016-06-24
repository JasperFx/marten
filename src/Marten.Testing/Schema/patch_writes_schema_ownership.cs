using System;
using Marten.Testing.Documents;
using Marten.Util;
using Npgsql;
using Xunit;

namespace Marten.Testing.Schema
{
    public class patch_writes_schema_ownership : IntegratedFixture
    {
        public patch_writes_schema_ownership()
        {
            using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                conn.Open();

                try
                {
                    conn.CreateCommand().Sql("create role george;").ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // don't care
                }


            }
        }

        [Fact]
        public void should_not_write_ownership_without_schema_owner_name()
        {
            StoreOptions(_ =>
            {
                _.DatabaseOwnerName = null;
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldNotContain("OWNER TO");
        }

        [Fact]
        public void should_write_ownership_with_the_schema_owner_name()
        {
            StoreOptions(_ =>
            {
                _.DatabaseOwnerName = "george";
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("ALTER TABLE public.mt_doc_user OWNER TO \"george\";");
        }

        [Fact]
        public void writes_ownership_on_table_in_the_case_of_a_patch()
        {
            StoreOptions(_ =>
            {
                _.DatabaseOwnerName = "george";
                _.Schema.For<User>();
            });

            theStore.Schema.EnsureStorageExists(typeof(User));



            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseOwnerName = "george";
                _.Schema.For<User>().Duplicate(x => x.UserName);
            }))
            {
                var patch = store2.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("ALTER TABLE public.mt_doc_user OWNER TO \"george\";");
            }
        }
    }
}