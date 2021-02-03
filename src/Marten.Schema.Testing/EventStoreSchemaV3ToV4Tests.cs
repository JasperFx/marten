using System;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    public class EventStoreSchemaV3ToV4Tests : IntegrationContext
    {
        [Fact]
        public void can_create_patch_for_event_store_schema_changes()
        {
            var store1 = Store(AutoCreate.All);
            store1.Tenancy.Default.EnsureStorageExists(typeof(StreamAction));

            SimulateEventStoreV3Schema();

            // create another store and check if the schema can be be auto updated
            using var store2 = Store(AutoCreate.CreateOrUpdate);

            var sql = store2.Schema.ToPatch().UpdateDDL;
            sql.ShouldContain($"alter table {_schemaName}.mt_events alter column version type bigint", Case.Insensitive);
            sql.ShouldContain($"alter table {_schemaName}.mt_streams alter column version type bigint", Case.Insensitive);
            sql.ShouldContain($"drop function if exists {_schemaName}.mt_append_event", Case.Insensitive);
        }

        [Fact]
        public void can_auto_update_event_store_schema_changes()
        {
            using var store1 = Store(AutoCreate.All);
            store1.Schema.ApplyAllConfiguredChangesToDatabase();

            SimulateEventStoreV3Schema();

            // create another store and check if the schema can be be auto updated
            using var store2 = Store(AutoCreate.CreateOrUpdate);

            Should.Throw<SchemaValidationException>(() =>
            {
                store2.Schema.AssertDatabaseMatchesConfiguration();
            });

            Should.NotThrow(() =>
            {
                store2.Schema.ApplyAllConfiguredChangesToDatabase();
                store2.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

        [Fact]
        public void should_not_have_v3_to_v4_patches_on_v4_schema()
        {
            using var store1 = Store(AutoCreate.All);
            store1.Tenancy.Default.EnsureStorageExists(typeof(StreamAction));

            // create another store and check if no v3 to v4 patches are generated
            using var store2 = Store(AutoCreate.CreateOrUpdate);

            var sql = store2.Schema.ToPatch().UpdateDDL;
            sql.ShouldNotContain($"alter table {_schemaName}.mt_events alter column version type bigint", Case.Insensitive);
            sql.ShouldNotContain($"alter table {_schemaName}.mt_streams alter column version type bigint", Case.Insensitive);
            sql.ShouldNotContain($"drop function if exists {_schemaName}.mt_append_event", Case.Insensitive);
        }

        private DocumentStore Store(AutoCreate autoCreate)
        {
            return DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = _schemaName;
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = autoCreate;
                _.EventGraph.EventMappingFor<MembersJoined>();
            });
        }

        private void SimulateEventStoreV3Schema()
        {
            // simulate to event store v3 schema with version fields as int
            using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            try
            {
                conn.Open();
                // version as integer in mt_events
                conn.CreateCommand($"alter table {_schemaName}.mt_events alter column version type int")
                    .ExecuteNonQuery();

                // version as integer in mt_streams
                conn.CreateCommand($"alter table {_schemaName}.mt_streams alter column version type int")
                    .ExecuteNonQuery();
                conn.CreateCommand($"create function {_schemaName}.mt_append_event(uuid, varchar, varchar, uuid[], varchar[], varchar[], jsonb[]) returns int language plpgsql as $$ begin return 1; end; $$;")
                    .ExecuteNonQuery();
            }
            finally
            {
                conn.Close();
            }
        }

        private readonly string _schemaName;

        public EventStoreSchemaV3ToV4Tests()
        {
            _schemaName = $"s_{Guid.NewGuid().ToString().Replace("-", "")}";
        }
    }
}
