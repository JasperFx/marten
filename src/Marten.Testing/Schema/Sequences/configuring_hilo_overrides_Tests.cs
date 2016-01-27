using Marten.Schema;
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

        [Fact]
        public void override_by_document_on_marten_registry()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Schema.For<IntDoc>().HiLoSettings(new HiloDef {Increment = 6, MaxLo = 66});
                _.Connection("something");
            });

            var mapping = store.Schema.MappingFor(typeof(IntDoc));

            var idStrategy = mapping.IdStrategy.ShouldBeOfType<HiloIdGeneration>();

            idStrategy.Increment.ShouldBe(6);
            idStrategy.MaxLo.ShouldBe(66);
        }

        [Fact]
        public void can_override_at_document_level_with_attribute()
        {
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.Increment = 3;
                _.HiloSequenceDefaults.MaxLo = 33;
                _.Connection("something");
            });

            var mapping = store.Schema.MappingFor(typeof(OverriddenHiloDoc));

            var idStrategy = mapping.IdStrategy.ShouldBeOfType<HiloIdGeneration>();

            idStrategy.Increment.ShouldBe(3);
            idStrategy.MaxLo.ShouldBe(33);
        }
    }

    [HiLoSequence(Increment = 3, MaxLo = 33)]
    public class OverriddenHiloDoc
    {
        public int Id { get; set; }
    }
}