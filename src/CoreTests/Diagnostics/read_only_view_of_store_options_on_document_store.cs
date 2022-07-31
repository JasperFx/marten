using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Diagnostics
{
    public class read_only_view_of_store_options_on_document_store : OneOffConfigurationsContext
    {
        public read_only_view_of_store_options_on_document_store()
        {
            StoreOptions(opts =>
            {
                opts.DatabaseSchemaName = "read_only";
                opts.Projections.Add<AllGood>();
                opts.Projections.Add<AllSync>();

                opts.RegisterDocumentType<User>();
                opts.RegisterDocumentType<Target>();

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
            theStore.As<IDocumentStore>().Options.Events.AllKnownEventTypes()
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
            theStore.As<IDocumentStore>().Options.DatabaseSchemaName.ShouldBe("read_only");
        }

        [Fact]
        public void can_retrieve_projections()
        {
            var readOnlyStoreOptions = theStore.As<IDocumentStore>().Options;
            var readOnlyEventStoreOptions = readOnlyStoreOptions.Events;
            readOnlyEventStoreOptions.Projections().Any().ShouldBeTrue();
        }

        [Fact]
        public void fetch_the_document_types()
        {
            theStore.As<IDocumentStore>().Options.AllKnownDocumentTypes().Any().ShouldBeTrue();
        }

        [Fact]
        public void find_existing_mapping()
        {
            var m1 = theStore.As<IDocumentStore>().Options.FindOrResolveDocumentType(typeof(User));
            var m2 = theStore.As<IDocumentStore>().Options.FindOrResolveDocumentType(typeof(User));

            m1.ShouldNotBeNull();
            m1.ShouldBeSameAs(m2);
        }

        [Fact]
        public void resolve_mapping_from_sub_class()
        {
            var root = theStore.As<IDocumentStore>().Options.FindOrResolveDocumentType(typeof(BaseballTeam));
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

    public class QuestStarted
    {
        public string Name { get; set; }
        public Guid Id { get; set; }

        public override string ToString()
        {
            return $"Quest {Name} started";
        }

        protected bool Equals(QuestStarted other)
        {
            return Name == other.Name && Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((QuestStarted) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, Id);
        }
    }

    public class QuestEnded
    {
        public string Name { get; set; }
        public Guid Id { get; set; }

        public override string ToString()
        {
            return $"Quest {Name} ended";
        }
    }


    public class AllSync: SingleStreamAggregation<MyAggregate>
    {
        public AllSync()
        {
            ProjectionName = "AllSync";
        }

        public MyAggregate Create(CreateEvent @event)
        {
            return new MyAggregate
            {
                ACount = @event.A,
                BCount = @event.B,
                CCount = @event.C,
                DCount = @event.D
            };
        }

        public void Apply(AEvent @event, MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount + 1,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount,
                Id = aggregate.Id
            };
        }

        public void Apply(MyAggregate aggregate, CEvent @event)
        {
            aggregate.CCount++;
        }

        public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount + 1,
                Id = aggregate.Id
            };
        }
    }

    public class AllGood: SingleStreamAggregation<MyAggregate>
    {
        public AllGood()
        {
            ProjectionName = "AllGood";
        }

        [MartenIgnore]
        public void RandomMethodName()
        {

        }

        public MyAggregate Create(CreateEvent @event)
        {
            return new MyAggregate
            {
                ACount = @event.A,
                BCount = @event.B,
                CCount = @event.C,
                DCount = @event.D
            };
        }

        public Task<MyAggregate> Create(CreateEvent @event, IQuerySession session)
        {
            return null;
        }

        public void Apply(AEvent @event, MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount + 1,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount,
                Id = aggregate.Id
            };
        }

        public void Apply(MyAggregate aggregate, CEvent @event)
        {
            aggregate.CCount++;
        }

        public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount + 1,
                Id = aggregate.Id
            };
        }
    }

    public class MyAggregate
    {
        public Guid Id { get; set; }

        public int ACount { get; set; }
        public int BCount { get; set; }
        public int CCount { get; set; }
        public int DCount { get; set; }
        public int ECount { get; set; }

        public string Created { get; set; }
        public string UpdatedBy { get; set; }
        public Guid EventId { get; set; }
    }

    public interface ITabulator
    {
        void Apply(MyAggregate aggregate);
    }

    public class AEvent : ITabulator
    {
        // Necessary for a couple tests. Let it go.
        public Guid Id { get; set; }

        public void Apply(MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public Guid Tracker { get; } = Guid.NewGuid();
    }

    public class BEvent : ITabulator
    {
        public void Apply(MyAggregate aggregate)
        {
            aggregate.BCount++;
        }
    }

    public class CEvent : ITabulator
    {
        public void Apply(MyAggregate aggregate)
        {
            aggregate.CCount++;
        }
    }

    public class DEvent : ITabulator
    {
        public void Apply(MyAggregate aggregate)
        {
            aggregate.DCount++;
        }
    }
    public class EEvent {}

    public class CreateEvent
    {
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int D { get; }

        public CreateEvent(int a, int b, int c, int d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }

}
