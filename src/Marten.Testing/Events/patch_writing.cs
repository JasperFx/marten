using Marten.Events;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events
{
    public class patch_writing: IntegrationContext
    {
        [Fact]
        public void generate_the_patch_with_drop_if_does_not_exist()
        {
            var schemaName = StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(MembersJoined));
            });

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain($"drop table if exists {schemaName}.mt_streams cascade;");
        }

        [Fact]
        public void generate_the_patch_with_drop_if_does_not_exist_in_a_different_schema()
        {
            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.DatabaseSchemaName = "events";
            });

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists events.mt_streams cascade;");
        }

        [Fact]
        public void no_rollback_if_the_table_already_existed()
        {
            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(MembersJoined));
            });

            theStore.Tenancy.Default.EnsureStorageExists(typeof(StreamAction));

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldNotContain("public.mt_streams");
        }

        public patch_writing(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
