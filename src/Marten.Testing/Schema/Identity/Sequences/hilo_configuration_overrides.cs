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
        public void can_establish_the_hilo_starting_point()
        {
            // SAMPLE: ResetHiloSequenceFloor
            var store = DocumentStore.For(ConnectionSource.ConnectionString);

            // Resets the minimum Id number for the IntDoc document
            // type to 2500
            store.Advanced.ResetHiloSequenceFloor<IntDoc>(2500);
            // ENDSAMPLE

            using (var session = store.OpenSession())
            {
                var doc1 = new IntDoc();
                var doc2 = new IntDoc();
                var doc3 = new IntDoc();

                session.Store(doc1, doc2, doc3);
                
                doc1.Id.ShouldBeGreaterThanOrEqualTo(2500);
                doc2.Id.ShouldBeGreaterThanOrEqualTo(2500);
                doc3.Id.ShouldBeGreaterThanOrEqualTo(2500);
            }
        }


        [Fact]
        public void default_everything()
        {
            var defaults = new HiloSettings();

            var store = DocumentStore.For(ConnectionSource.ConnectionString);
            var mapping = store.Storage.MappingFor(typeof (IntDoc));

            var idStrategy = mapping.ToIdAssignment<IntDoc>(store.DefaultTenant)
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

            var mapping = store.Storage.MappingFor(typeof(IntDoc));

            var idStrategy = mapping.ToIdAssignment<IntDoc>(store.DefaultTenant)
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

            var mapping = store.Storage.MappingFor(typeof(IntDoc));

            var idStrategy = mapping.ToIdAssignment<IntDoc>(store.DefaultTenant)
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

            var mapping = store.Storage.MappingFor(typeof(OverriddenHiloDoc));


            var idStrategy = mapping.ToIdAssignment<OverriddenHiloDoc>(store.DefaultTenant)
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