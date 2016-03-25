using Marten.Schema;
using Marten.Schema.Sequences;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Sequences
{
    public class SequenceFactoryTests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;

        public SequenceFactoryTests()
        {
            theContainer.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            _schema = theContainer.GetInstance<IDocumentSchema>();

            _schema.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            _schema.SchemaFunctionNames().ShouldContain("public.mt_get_next_hi");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            _schema.SchemaFunctionNames().ShouldContain("public.mt_get_next_hi");
        }
    }

    public class SequenceFactoryOnOtherDatabaseSchemaTests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;

        public SequenceFactoryOnOtherDatabaseSchemaTests()
        {
            theContainer.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            _schema = theContainer.GetInstance<IDocumentSchema>();

            _schema.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        protected override void StoreOptions(StoreOptions options)
        {
            options.DatabaseSchemaName = "other";
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            _schema.SchemaTableNames().ShouldContain("other.mt_hilo");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            _schema.SchemaFunctionNames().ShouldContain("other.mt_get_next_hi");
        }
    }
}