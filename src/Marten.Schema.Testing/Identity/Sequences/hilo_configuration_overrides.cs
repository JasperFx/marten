using Baseline;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Identity.Sequences
{
    [Collection("sequences")]
    public class hilo_configuration_overrides
    {
        [Fact]
        public void can_establish_the_hilo_starting_point()
        {
            // SAMPLE: ResetHiloSequenceFloor
            var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "sequences";
            });

            // Resets the minimum Id number for the IntDoc document
            // type to 2500
            store.Tenancy.Default.ResetHiloSequenceFloor<IntDoc>(2500);
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

            var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "sequences";
            });

            var mapping = store.Storage.MappingFor(typeof (IntDoc));

            mapping.ToIdAssignment<IntDoc>(store.Tenancy.Default)
                .As<IdAssigner<IntDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();

            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).MaxLo.ShouldBe(defaults.MaxLo);
        }

        [Fact]
        public void override_the_global_settings()
        {
            // SAMPLE: configuring-global-hilo-defaults
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.MaxLo = 55;
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = "sequences";
            });
            // ENDSAMPLE

            var mapping = store.Storage.MappingFor(typeof(IntDoc));

            var idStrategy = mapping.ToIdAssignment<IntDoc>(store.Tenancy.Default)
                .As<IdAssigner<IntDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();

            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).MaxLo.ShouldBe(55);
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

                _.DatabaseSchemaName = "sequences";
            });
            // ENDSAMPLE

            var mapping = store.Storage.MappingFor(typeof(IntDoc));

            mapping.ToIdAssignment<IntDoc>(store.Tenancy.Default)
                .As<IdAssigner<IntDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();

            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).MaxLo.ShouldBe(66);
            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).As<HiloSequence>().EntityName.ShouldBe("IntDoc");
        }

        [Fact]
        public void can_override_at_document_level_with_attribute()
        {
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.MaxLo = 33;
                _.Connection(ConnectionSource.ConnectionString);

                _.DatabaseSchemaName = "sequences";
            });

            var mapping = store.Storage.MappingFor(typeof(IntDoc));

            mapping.ToIdAssignment<IntDoc>(store.Tenancy.Default)
                .As<IdAssigner<IntDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();

            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).MaxLo.ShouldBe(33);
            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).As<HiloSequence>().EntityName.ShouldBe("IntDoc");


            mapping = store.Storage.MappingFor(typeof(OverriddenHiloDoc));

            mapping.ToIdAssignment<OverriddenHiloDoc>(store.Tenancy.Default)
                .As<IdAssigner<OverriddenHiloDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();

            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(OverriddenHiloDoc)).MaxLo.ShouldBe(66);
            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(OverriddenHiloDoc)).As<HiloSequence>().EntityName.ShouldBe("Entity");
        }

        [Fact]
        public void set_default_sequencename()
        {
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.MaxLo = 33;
                _.HiloSequenceDefaults.SequenceName = "ID";
                _.Connection(ConnectionSource.ConnectionString);

                _.DatabaseSchemaName = "sequences";
            });

            var mapping = store.Storage.MappingFor(typeof(IntDoc));
            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).MaxLo.ShouldBe(33);

            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(IntDoc)).As<HiloSequence>().EntityName.ShouldBe("ID");

            mapping = store.Storage.MappingFor(typeof(OverriddenHiloDoc));

            mapping.ToIdAssignment<OverriddenHiloDoc>(store.Tenancy.Default)
                .As<IdAssigner<OverriddenHiloDoc, int>>().Generator
                .ShouldBeOfType<IntHiloGenerator>();

            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(OverriddenHiloDoc)).MaxLo.ShouldBe(66);
            store.Tenancy.Default.Sequences
                .SequenceFor(typeof(OverriddenHiloDoc)).As<HiloSequence>().EntityName.ShouldBe("Entity");
        }

        [Fact]
        public void create_docs_with_global_id()
        {
            // SAMPLE: configuring-global-hilo-defaults-sequencename
            var store = DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.SequenceName = "Entity";
                _.Connection(ConnectionSource.ConnectionString);

                _.DatabaseSchemaName = "sequences";
            });
            // ENDSAMPLE
            using (var session = store.OpenSession())
            {
                var doc1 = new IntDoc();
                var doc2 = new Int2Doc();
                var doc3 = new IntDoc();
                var doc4 = new Int2Doc();

                session.Store(doc1);
                session.Store(doc2);
                session.Store(doc3);
                session.Store(doc4);

                doc1.Id.ShouldBeGreaterThanOrEqualTo(1);
                doc2.Id.ShouldBe(doc1.Id + 1);
                doc3.Id.ShouldBe(doc2.Id + 1);
                doc4.Id.ShouldBe(doc3.Id + 1);
            }
        }



    }

    public class Int2Doc
    {
        public int Id { get; set; }
    }


    // SAMPLE: overriding-hilo-with-attribute
    [HiloSequence(MaxLo = 66, SequenceName = "Entity")]
    public class OverriddenHiloDoc
    {
        public int Id { get; set; }
    }
    // ENDSAMPLE
}
