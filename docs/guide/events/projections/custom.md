# Custom Projections

## Multistream Projections using ViewProjection

The `ViewProjection` class is an implementation of the `IProjection` that can handle building a projection from multiple streams.

This can be setup from configuration like:

<[sample:viewprojection-from-configuration]>

or through a class like:

<[sample:viewprojection-from-class]>

`ProjectEvent` by default takes two parameters:

* property from event that will be used as projection document selector,
* apply method that describes the projection by itself.

`DeleteEvent` takes the first parameter - as by the nature of this method it's only needed to select which document should be deleted.

Both methods may also select multiple Ids:

* `ProjectEvent` if a `List<TId>` is passed, the handler method will be called for each Id in the collection.
* `DeleteEvent` if a `List<TId>` is passed, then each document tied to the Id in the collection will be removed.

Each of these methods take various overloads that allow selecting the Id field implicitly, through a property or through two different Func's `Func<IDocumentSession, TEvent, TId>` and `Func<TEvent, TId>`.

::: warning
Projection class needs to have **Id** property with public getter or property marked with **Identity** attribute.

It comes of the way how Marten handles projection mechanism:

1. Try to find document that has the same **Id** as the value of the property selected from event (so eg. for **UserCreated** event it will be **UserId**).
1. If no such document exists, then new record needs to be created. Marten by default tries to use the **default constructor**

    The default constructor doesn't have to be public, it can also be private or protected.

    If the class does not have a default constructor then it creates an uninitialized object (see <https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.formatterservices.getuninitializedobject?view=netframework-4.8> for more info).

    Because of that, no member initializers will be run so all of them need to be initialized in the event handler methods.
1. If a document with such **Id** was found then it's being loaded from database.
1. Document is updated with the logic defined in the **ViewProjection** (using expression from second **ProjectEvent** parameter).
1. Created or updated document is upserted to database.
:::

### Using event meta data

If additional Marten event details are needed, then events can use the `ProjectionEvent<>` generic when setting them up with `ProjectEvent`. `ProjectionEvent` exposes the Marten Id, Version, Timestamp and Data.

<!-- snippet: sample_viewprojection-from-class-with-eventdata -->
<a id='snippet-sample_viewprojection-from-class-with-eventdata'></a>
```cs
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

public class LapViewProjection: ViewProjection<Lap, Guid>
{
    public LapViewProjection()
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/custom_transformation_of_events.cs#L88-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_viewprojection-from-class-with-eventdata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Injecting helpers classes

ViewProjections instances are created (by default) during the `DocumentStore` initialization. Marten gives also possible to register them with factory method. With such registration projections are created on runtime during the events application. Thanks to that it's possible to setup custom creation logic or event connect dependency injection mechanism.

<[sample:viewprojection-from-class-with-injection-configuration]>

By convention it's needed to provide the default constructor with projections definition and other with code injection (that calls the default constructor).

<[sample:viewprojection-from-class-with-injection]>

### Using async projections

It's also possible to use async version of `ProjectEvent`. Using `ProjectEventAsync` gives possibility to call the async apis (from Marten or other frameworks) to get better resources utilization.

Sample usage could be loading other document/projection to create denormalized view.

<[sample:viewprojection-from-class-async-with-load]>

::: warning
Note the "async projections" term in this context means that they are using the .NET async/await mechanism that helps to use threads efficiently without locking them.

It does not refer to async projections as eventually consistent. Such option provides [Async Daemon](/guide/events/projections/async-daemon).
:::

### Update only projection

ProjectEvent overloads contain additional boolean parameter **onlyUpdate**. By default, it's set to false which mean that Marten will do create or update operation with projection view.

Lets' look on the following scenario of the projection that manages the newsletter Subscription.

1. New reader subscribed to newsletter and _ReaderSubscribed_ event was published. Projection handles the event and creates new view record in database.
1. User opened newsletter and _NewsletterOpened_ event was published. Projection handles the event and updates view in database with incremented opens count.
1. User unsubscribed from newsletter and _ReaderUnsubscribed_ event was published. Projection removed the view from database (because we market it with `DeleteEvent`).
1. User opened newsletter after unsubscribing and _NewsletterOpened_ event was published. As there is no record in database if we use the default behaviour then new record will be created with only data that are applied for the _NewsletterOpened_ event. That's might create views with unexpected state. <u>In that case, **onlyUpdate** set to **true** should be used. Having that, if the view does not exist then the event will not be projected and new view record will not be created in database.</u>

<!-- snippet: sample_viewprojection-with-update-only -->
<a id='snippet-sample_viewprojection-with-update-only'></a>
```cs
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

public class NewsletterSubscriptionProjection : ViewProjection<NewsletterSubscription, Guid>
{
    public NewsletterSubscriptionProjection()
    {
        Identity<SubscriptionEvent>(x => x.SubscriptionId);

        DeleteEvent<ReaderUnsubscribed>();
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/custom_transformation_of_events.cs#L134-L215' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_viewprojection-with-update-only' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
