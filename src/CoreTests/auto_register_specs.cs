using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Events;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

public class auto_register_specs : OneOffConfigurationsContext
{
    private readonly EventGraph theEvents;

    public auto_register_specs()
    {
        StoreOptions(opts =>
        {
            opts.ApplicationAssembly = GetType().Assembly;
            opts.AutoRegister(x =>
            {
                x.EventsImplement<IDomainEvent>();
                x.EventsImplement<IIntegrationEvent>();
            });
        });

        theEvents = (EventGraph)theStore.Options.Events;
    }

    [Fact]
    public void discovered_events_based_on_filters()
    {
        theEvents.AllKnownEventTypes().ShouldContain(x => x.EventType == typeof(DomainEvent1));
        theEvents.AllKnownEventTypes().ShouldContain(x => x.EventType == typeof(DomainEvent2));
        theEvents.AllKnownEventTypes().ShouldContain(x => x.EventType == typeof(DomainEvent3));
        theEvents.AllKnownEventTypes().ShouldContain(x => x.EventType == typeof(IntegrationEvent1));
        theEvents.AllKnownEventTypes().ShouldContain(x => x.EventType == typeof(IntegrationEvent2));
    }

    [Fact]
    public void find_events_by_attribute()
    {
        theEvents.AllKnownEventTypes().ShouldContain(x => x.EventType == typeof(IntegrationEvent3));

        theEvents.EventMappingFor<IntegrationEvent4>()
            .Alias.ShouldBe("four");
    }

    [Fact]
    public void discover_documents_by_attribute()
    {
        theStore.Options.Storage.AllDocumentMappings.ShouldContain(x => x.DocumentType == typeof(DiscoveredDocument1));
        theStore.Options.Storage.AllDocumentMappings.ShouldContain(x => x.DocumentType == typeof(DiscoveredDocument2));
    }

    [Fact]
    public void finds_compiled_query_types()
    {
        theStore.Options.CompiledQueryTypes.ShouldContain(x => x == typeof(UsersByFirstName));
    }
}

[MartenDocument]
public class DiscoveredDocument1
{
    public Guid Id { get; set; }
}

[MartenDocument]
public class DiscoveredDocument2
{
    public Guid Id { get; set; }
}

public interface IDomainEvent{}
public interface IIntegrationEvent{}

public class DomainEvent1: IDomainEvent;
public class DomainEvent2: IDomainEvent;
public class DomainEvent3: IDomainEvent;

public class IntegrationEvent1: IIntegrationEvent;
public class IntegrationEvent2: IIntegrationEvent;

[MartenEvent]
public class IntegrationEvent3{}

[MartenEvent(Alias = "four")]
public class IntegrationEvent4{}

public class UsersByFirstName: ICompiledListQuery<User>
{
    public static int Count;
    public string FirstName { get; set; }

    public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
    {
        return query => query.Where(x => x.FirstName == FirstName);
    }
}


