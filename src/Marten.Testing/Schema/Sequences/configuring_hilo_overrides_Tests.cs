using Marten.Schema.Sequences;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Sequences
{
    public class configuring_hilo_overrides_Tests
    {
        [Fact]
        public void default_everything()
        {
            var defaults = new HiloDef();

            var store = DocumentStore.For("something");
            var mapping = store.Schema.MappingFor(typeof (IntDoc));

            var idStrategy = mapping.IdStrategy.ShouldBeOfType<HiloIdGeneration>();

            idStrategy.Increment.ShouldBe(defaults.Increment);
            idStrategy.MaxLo.ShouldBe(defaults.MaxLo);
        }

        [Fact]
        public void override_the_global_settings()
        {
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.Increment = 2;
                _.HiloSequenceDefaults.MaxLo = 55;
                _.Connection("something");
            });

            var mapping = store.Schema.MappingFor(typeof(IntDoc));

            var idStrategy = mapping.IdStrategy.ShouldBeOfType<HiloIdGeneration>();

            idStrategy.Increment.ShouldBe(2);
            idStrategy.MaxLo.ShouldBe(55);
        }
    }
}