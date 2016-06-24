using System;
using Marten.Testing.Documents;
using Marten.Testing.Events;
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

        [Fact]
        public void ownership_on_patch_function_if_database_owner()
        {
            StoreOptions(_ =>
            {
                _.DatabaseOwnerName = "george";
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("ALTER FUNCTION public.mt_transform_patch_doc(JSONB, JSONB) OWNER TO \"george\";");
        }

        [Fact]
        public void event_store_tables_get_ownership_if_schema_owner()
        {
            StoreOptions(_ =>
            {
                _.Events.DatabaseSchemaName = "events";
                _.DatabaseOwnerName = "george";
                _.Events.AddEventType(typeof(MembersJoined));
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("ALTER TABLE events.mt_streams OWNER TO \"george\";");
            patch.UpdateDDL.ShouldContain("ALTER TABLE events.mt_events OWNER TO \"george\";");
            patch.UpdateDDL.ShouldContain("ALTER TABLE events.mt_event_progression OWNER TO \"george\";");
            patch.UpdateDDL.ShouldContain("ALTER SEQUENCE events.mt_events_sequence OWNER TO \"george\";");
            patch.UpdateDDL.ShouldContain("ALTER FUNCTION events.mt_append_event(uuid, varchar, uuid[], varchar[], jsonb[]) OWNER TO \"george\";");
            patch.UpdateDDL.ShouldContain("ALTER FUNCTION events.mt_mark_event_progression(varchar, bigint) OWNER TO \"george\";");
        }

        [Fact]
        public void event_store_tables_do_not_get_ownership_if_no_schema_owner()
        {
            StoreOptions(_ =>
            {
                _.Events.DatabaseSchemaName = "events";
                _.DatabaseOwnerName = null;
                _.Events.AddEventType(typeof(MembersJoined));
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldNotContain("ALTER TABLE events.mt_streams OWNER TO \"george\";");
            patch.UpdateDDL.ShouldNotContain("ALTER TABLE events.mt_events OWNER TO \"george\";");
            patch.UpdateDDL.ShouldNotContain("ALTER TABLE events.mt_event_progression OWNER TO \"george\";");
            patch.UpdateDDL.ShouldNotContain("ALTER SEQUENCE events.mt_events_sequence OWNER TO \"george\";");
            patch.UpdateDDL.ShouldNotContain("ALTER FUNCTION events.mt_append_event(uuid, varchar, uuid[], varchar[], jsonb[]) OWNER TO \"george\";");
            patch.UpdateDDL.ShouldNotContain("ALTER FUNCTION events.mt_mark_event_progression(varchar, bigint) OWNER TO \"george\";");
        }

        [Fact]
        public void hilo_tables_get_ownership_if_schema_owner()
        {
            StoreOptions(_ =>
            {
                _.DatabaseOwnerName = "george";
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("ALTER TABLE public.mt_hilo OWNER TO \"george\";");
            patch.UpdateDDL.ShouldContain("ALTER FUNCTION public.mt_get_next_hi(varchar) OWNER TO \"george\";");



        }
    }
}