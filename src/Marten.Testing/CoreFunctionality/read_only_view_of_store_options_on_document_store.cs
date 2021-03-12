using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class read_only_view_of_store_options_on_document_store : IDisposable
    {
        private IDocumentStore theStore;

        public read_only_view_of_store_options_on_document_store()
        {
            theStore = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "read_only";
                opts.Events.Projections.Add<AllGood>();
                opts.Events.Projections.Add<AllSync>();

                opts.RegisterDocumentType<User>();
                opts.RegisterDocumentType<Target>();

                // Let Marten derive the transform name from the filename
                opts.Transforms.LoadFile("get_fullname.js");

                // Explicitly define the transform name yourself
                opts.Transforms.LoadFile("default_username.js", "set_default_username");

                opts.Schema.For<Squad>()
                    .AddSubClass<BaseballTeam>()
                    .AddSubClass<BasketballTeam>()
                    .AddSubClass<FootballTeam>();

                opts.Events.AddEventType(typeof(QuestStarted));
                opts.Events.AddEventType(typeof(QuestEnded));

            });
        }

        [Fact]
        public void can_find_all_event_types()
        {
            theStore.Options.Events.AllKnownEventTypes()
                .Any()
                .ShouldBeTrue();
        }

        public void Dispose()
        {
            theStore?.Dispose();
        }

        [Fact]
        public void have_the_readonly_options()
        {
            theStore.Options.DatabaseSchemaName.ShouldBe("read_only");
        }

        [Fact]
        public void can_retrieve_projections()
        {
            var readOnlyStoreOptions = theStore.Options;
            var readOnlyEventStoreOptions = readOnlyStoreOptions.Events;
            readOnlyEventStoreOptions.Projections().Any().ShouldBeTrue();
        }

        [Fact]
        public void can_get_transforms()
        {
            theStore.Options.Transforms().Any().ShouldBeTrue();
        }

        [Fact]
        public void fetch_the_document_types()
        {
            theStore.Options.AllKnownDocumentTypes().Any().ShouldBeTrue();
        }

        [Fact]
        public void find_existing_mapping()
        {
            var m1 = theStore.Options.FindOrResolveDocumentType(typeof(User));
            var m2 = theStore.Options.FindOrResolveDocumentType(typeof(User));

            m1.ShouldNotBeNull();
            m1.ShouldBeTheSameAs(m2);
        }

        [Fact]
        public void resolve_mapping_from_sub_class()
        {
            var root = theStore.Options.FindOrResolveDocumentType(typeof(BaseballTeam));
            root.DocumentType.ShouldBe(typeof(Squad));

            root.SubClasses.Any(x => x.DocumentType == typeof(BaseballTeam))
                .ShouldBeTrue();
        }


        public class Squad
        {
            public string Id { get; set; }
        }

        public class BasketballTeam : Squad { }

        public class FootballTeam : Squad { }

        public class BaseballTeam : Squad { }
    }
}
