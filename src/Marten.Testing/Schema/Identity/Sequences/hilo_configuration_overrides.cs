using Baseline;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class hilo_configuration_overrides
    {
        [Fact]
        public void default_everything()
        {
            var defaults = new HiloSettings();

            var store = DocumentStore.For(ConnectionSource.ConnectionString);
            var mapping = store.Schema.MappingFor(typeof (IntDoc));

            var idStrategy = mapping.ToIdAssignment<IntDoc>(store.Schema)
                .As<IdAssigner<IntDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();



            idStrategy.Sequence.MaxLo.ShouldBe(defaults.MaxLo);
        }

        [Fact]
        public void override_the_global_settings()
        {
            // SAMPLE: configuring-global-hilo-defaults
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.MaxLo = 55;
                _.Connection(ConnectionSource.ConnectionString);
            });
            // ENDSAMPLE

            var mapping = store.Schema.MappingFor(typeof(IntDoc));

            var idStrategy = mapping.ToIdAssignment<IntDoc>(store.Schema)
                .As<IdAssigner<IntDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();



            idStrategy.Sequence.MaxLo.ShouldBe(55);
        }

        [Fact]
        public void override_by_document_on_marten_registry()
        {
            // SAMPLE: overriding-hilo-with-marten-registry
            var store = DocumentStore.For(_ =>
            {
                // Overriding the Hilo settings for the document type "IntDoc"
                _.Schema.For<IntDoc>()
                    .HiloSettings(new HiloSettings {MaxLo = 66});

                _.Connection(ConnectionSource.ConnectionString);
            });
            // ENDSAMPLE

            var mapping = store.Schema.MappingFor(typeof(IntDoc));

            var idStrategy = mapping.ToIdAssignment<IntDoc>(store.Schema)
                .As<IdAssigner<IntDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();



            idStrategy.Sequence.MaxLo.ShouldBe(66);
        }

        [Fact]
        public void can_override_at_document_level_with_attribute()
        {
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.MaxLo = 33;
                _.Connection(ConnectionSource.ConnectionString);
            });

            var mapping = store.Schema.MappingFor(typeof(OverriddenHiloDoc));


            var idStrategy = mapping.ToIdAssignment<OverriddenHiloDoc>(store.Schema)
                .As<IdAssigner<OverriddenHiloDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();



            idStrategy.Sequence.MaxLo.ShouldBe(33);
        }
    }

    // SAMPLE: overriding-hilo-with-attribute
    [HiloSequence(MaxLo = 33)]
    public class OverriddenHiloDoc
    {
        public int Id { get; set; }
    }
    // ENDSAMPLE
}