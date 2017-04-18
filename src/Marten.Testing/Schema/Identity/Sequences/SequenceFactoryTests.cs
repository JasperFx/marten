using Baseline;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    [Collection("DefaultSchema")]
    public class SequenceFactoryTests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;

        public SequenceFactoryTests()
        {
            _schema = theStore.Schema;

            theStore.DefaultTenant.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            _schema.DbObjects.Functions().ShouldContain("public.mt_get_next_hi");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            _schema.DbObjects.Functions().ShouldContain("public.mt_get_next_hi");
        }


    }

    public class SequenceFactory_patch_generation : IntegratedFixture
    {
        [Fact]
        public void generate_drop_part_of_patch()
        {
            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists public.mt_hilo cascade;");
        }

        [Fact]
        public void generate_drop_part_of_patch_different_schema()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");
            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists other.mt_hilo cascade;");
        }

    }

    public class SequenceFactoryOnOtherDatabaseSchemaTests : IntegratedFixture
    {
        public SequenceFactoryOnOtherDatabaseSchemaTests()
        {
            StoreOptions(x => x.DatabaseSchemaName = "seq_other");

            theStore.DefaultTenant.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            theStore.Schema.DbObjects.SchemaTables().ShouldContain("seq_other.mt_hilo");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            theStore.Schema.DbObjects.Functions().ShouldContain("seq_other.mt_get_next_hi");
        }
    }
}