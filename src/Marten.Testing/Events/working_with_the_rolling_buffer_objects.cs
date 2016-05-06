using System.Linq;
using Baseline;
using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class working_with_the_rolling_buffer_objects : IntegratedFixture
    {
        [Fact]
        public void can_build_the_rolling_buffer_objects_in_the_default_schmema()
        {
            theStore.EventStore.RebuildEventStoreSchema();
            theStore.EventStore.As<EventStoreAdmin>().InitializeTheRollingBuffer();

            var tables = theStore.Schema.DbObjects.SchemaTables().Select(x => x.QualifiedName).ToArray();

            tables.ShouldContain("public.mt_rolling_buffer");
            tables.ShouldContain("public.mt_options");

            var functions = theStore.Schema.DbObjects.SchemaFunctionNames().Select(x => x.QualifiedName).ToArray();

            functions.ShouldContain("public.mt_reset_rolling_buffer_size");
            functions.ShouldContain("public.mt_seed_rolling_buffer");
            functions.ShouldContain("public.mt_append_rolling_buffer");
        }

        [Fact]
        public void can_build_the_rolling_buffer_objects_in_a_different_schmema()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");

            theStore.EventStore.RebuildEventStoreSchema();
            theStore.EventStore.As<EventStoreAdmin>().InitializeTheRollingBuffer();

            var tables = theStore.Schema.DbObjects.SchemaTables().Select(x => x.QualifiedName).ToArray();

            tables.ShouldContain("other.mt_rolling_buffer");
            tables.ShouldContain("other.mt_options");

            var functions = theStore.Schema.DbObjects.SchemaFunctionNames().Select(x => x.QualifiedName).ToArray();

            functions.ShouldContain("other.mt_reset_rolling_buffer_size");
            functions.ShouldContain("other.mt_seed_rolling_buffer");
            functions.ShouldContain("other.mt_append_rolling_buffer");
        }
    }
}