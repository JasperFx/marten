using System.Linq;
using System.Threading.Tasks;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Identity.Sequences
{
    [Collection("DefaultSchema")]
    public class SequenceFactoryTests : IntegrationContext
    {
        private readonly IDocumentSchema _schema;

        public SequenceFactoryTests()
        {
            _schema = theStore.Schema;

            theStore.Tenancy.Default.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public async Task can_create_table_on_fly_if_necessary()
        {
            (await theStore.Tenancy.Default.Functions()).Select(x => x.QualifiedName).ShouldContain("public.mt_get_next_hi");
        }

        [Fact]
        public async Task can_create_function_on_fly_if_necessary()
        {
            (await theStore.Tenancy.Default.Functions()).Select(x => x.QualifiedName).ShouldContain("public.mt_get_next_hi");
        }


    }


    public class SequenceFactoryOnOtherDatabaseSchemaTests : IntegrationContext
    {
        public SequenceFactoryOnOtherDatabaseSchemaTests()
        {
            StoreOptions(x => x.DatabaseSchemaName = "seq_other");

            theStore.Tenancy.Default.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public async Task can_create_table_on_fly_if_necessary()
        {
            (await theStore.Tenancy.Default.SchemaTables()).Select(x => x.QualifiedName).ShouldContain("seq_other.mt_hilo");
        }

        [Fact]
        public async Task can_create_function_on_fly_if_necessary()
        {
            (await theStore.Tenancy.Default.Functions()).Select(x => x.QualifiedName).ShouldContain("seq_other.mt_get_next_hi");
        }
    }
}
