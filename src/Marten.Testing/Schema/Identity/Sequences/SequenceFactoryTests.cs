using Baseline;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    [Collection("DefaultSchema")]
    public class SequenceFactoryTests : IntegrationContext
    {
        private readonly IDocumentSchema _schema;

        public SequenceFactoryTests(DefaultStoreFixture fixture) : base(fixture)
        {
            _schema = theStore.Schema;

            theStore.Tenancy.Default.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            theStore.Tenancy.Default.DbObjects.Functions().ShouldContain("public.mt_get_next_hi");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            theStore.Tenancy.Default.DbObjects.Functions().ShouldContain("public.mt_get_next_hi");
        }


    }


    public class SequenceFactoryOnOtherDatabaseSchemaTests : IntegrationContext
    {
        public SequenceFactoryOnOtherDatabaseSchemaTests(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(x => x.DatabaseSchemaName = "seq_other");

            theStore.Tenancy.Default.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            theStore.Tenancy.Default.DbObjects.SchemaTables().ShouldContain("seq_other.mt_hilo");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            theStore.Tenancy.Default.DbObjects.Functions().ShouldContain("seq_other.mt_get_next_hi");
        }
    }
}
