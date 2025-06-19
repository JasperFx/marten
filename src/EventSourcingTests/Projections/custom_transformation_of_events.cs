using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Projections;

public class project_events_from_multiple_streams_into_view: OneOffConfigurationsContext
{
    private static readonly Guid streamId = Guid.NewGuid();
    private static readonly Guid streamId2 = Guid.NewGuid();

    private readonly QuestStarted started = new QuestStarted { Id = streamId, Name = "Find the Orb" };
    private readonly QuestStarted started2 = new QuestStarted { Id = streamId2, Name = "Find the Orb 2.0" };
    private readonly MonsterQuestsAdded monsterQuestsAdded = new MonsterQuestsAdded { QuestIds = new List<Guid> { streamId, streamId2 }, Name = "Dragon" };
    private readonly MonsterQuestsRemoved monsterQuestsRemoved = new MonsterQuestsRemoved { QuestIds = new List<Guid> { streamId, streamId2 }, Name = "Dragon" };
    private readonly QuestEnded ended = new QuestEnded { Id = streamId, Name = "Find the Orb" };
    private readonly MembersJoined joined = new MembersJoined { QuestId = streamId, Day = 2, Location = "Faldor's Farm", Members = ["Garion", "Polgara", "Belgarath"] };
    private readonly MonsterSlayed slayed1 = new MonsterSlayed { QuestId = streamId, Name = "Troll" };
    private readonly MonsterSlayed slayed2 = new MonsterSlayed { QuestId = streamId, Name = "Dragon" };
    private readonly MonsterDestroyed destroyed = new MonsterDestroyed { QuestId = streamId, Name = "Troll" };
    private readonly MembersDeparted departed = new MembersDeparted { QuestId = streamId, Day = 5, Location = "Sendaria", Members = ["Silk", "Barak"] };
    private readonly MembersJoined joined2 = new MembersJoined { QuestId = streamId, Day = 5, Location = "Sendaria", Members = ["Silk", "Barak"] };



    [Fact]
    public async Task updateonly_event_for_custom_view_projection_should_not_create_new_document()
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
            opts.Schema.For<NewsletterSubscription>().MultiTenanted();
            opts.Projections.Add(new NewsletterSubscriptionProjection(), ProjectionLifecycle.Inline);

        });

        var subscriptionId = Guid.NewGuid();
        var newsletterId = Guid.NewGuid();
        var readerId = Guid.NewGuid();

        var readerSubscribed = new ReaderSubscribed(subscriptionId, newsletterId, readerId, "John Doe");
        theSession.Events.StartStream<NewsletterSubscription>(streamId, readerSubscribed);
        await theSession.SaveChangesAsync();
        var subscription = await theSession.LoadAsync<NewsletterSubscription>(subscriptionId);
        subscription.ShouldNotBeNull();

        var newsletterOpened = new NewsletterOpened(subscriptionId, DateTime.Now);
        theSession.Events.Append(subscriptionId, newsletterOpened);
        await theSession.SaveChangesAsync();
        subscription = await theSession.LoadAsync<NewsletterSubscription>(subscriptionId);
        subscription.ShouldNotBeNull();

        var readerUnsubscribed = new ReaderUnsubscribed(subscriptionId);
        theSession.Events.Append(subscriptionId, readerUnsubscribed);
        await theSession.SaveChangesAsync();
        subscription = await theSession.LoadAsync<NewsletterSubscription>(subscriptionId);
        subscription.ShouldBeNull();

    }

}

public class QuestView
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class PersistedView
{
    public Guid Id { get; set; }
    public List<object> Events { get; } = new List<object>();
    public List<Guid> StreamIdsForEvents { get; set; } = new List<Guid>();
}


#region sample_viewprojection-from-class-with-eventdata

public class Lap
{
    public Guid Id { get; set; }

    public DateTimeOffset? Start { get; set; }

    public DateTimeOffset? End { get; set; }
}

public abstract class LapEvent
{
    public Guid LapId { get; set; }
}

public class LapStarted : LapEvent
{

}

public class LapFinished : LapEvent
{

}

public class LapMultiStreamProjection: MultiStreamProjection<Lap, Guid>
{
    public LapMultiStreamProjection()
    {
        // This tells the projection how to "split" the events
        // and identify the document. It should be able to use
        // a base class or interface. Can have multiple Identity()
        // calls for different events.
        Identity<LapEvent>(x => x.LapId);
    }

    public void Apply(Lap view, IEvent<LapStarted> eventData) =>
        view.Start = eventData.Timestamp;

    public void Apply(Lap view, IEvent<LapFinished> eventData) =>
        view.End = eventData.Timestamp;
}

#endregion

#region sample_viewprojection-with-update-only

public abstract class SubscriptionEvent
{
    public Guid SubscriptionId { get; set; }
}

public class NewsletterSubscription
{
    public Guid Id { get; set; }

    public Guid NewsletterId { get; set; }

    public Guid ReaderId { get; set; }

    public string FirstName { get; set; }

    public int OpensCount { get; set; }
}

public class ReaderSubscribed : SubscriptionEvent
{
    public Guid NewsletterId { get; }

    public Guid ReaderId { get; }

    public string FirstName { get; }

    public ReaderSubscribed(Guid subscriptionId, Guid newsletterId, Guid readerId, string firstName)
    {
        SubscriptionId = subscriptionId;
        NewsletterId = newsletterId;
        ReaderId = readerId;
        FirstName = firstName;
    }
}

public class NewsletterOpened : SubscriptionEvent
{
    public DateTime OpenedAt { get; }

    public NewsletterOpened(Guid subscriptionId, DateTime openedAt)
    {
        SubscriptionId = subscriptionId;
        OpenedAt = openedAt;
    }
}

public class ReaderUnsubscribed : SubscriptionEvent
{

    public ReaderUnsubscribed(Guid subscriptionId)
    {
        SubscriptionId = subscriptionId;
    }
}

public class NewsletterSubscriptionProjection : MultiStreamProjection<NewsletterSubscription, Guid>
{
    public NewsletterSubscriptionProjection()
    {
        Identity<SubscriptionEvent>(x => x.SubscriptionId);

        DeleteEvent<ReaderUnsubscribed>();

        TenancyGrouping = TenancyGrouping.AcrossTenants;
    }

    public void Apply(NewsletterSubscription view, ReaderSubscribed @event)
    {
        view.Id = @event.SubscriptionId;
        view.NewsletterId = @event.NewsletterId;
        view.ReaderId = @event.ReaderId;
        view.FirstName = @event.FirstName;
        view.OpensCount = 0;
    }

    public void Apply(NewsletterSubscription view, NewsletterOpened @event)
    {
        view.OpensCount++;
    }
}

#endregion

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public void Apply(ProjectStarted e)
    {
        Id = e.Id;
        Name = e.Name;
    }

    public void Apply(ProjectClosed e)
    {
    }
}

public class ProjectStarted
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class ProjectClosed
{
    public Guid Id { get; set; }
}
