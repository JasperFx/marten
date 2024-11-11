using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public class EventProjectionTests: OneOffConfigurationsContext, IAsyncLifetime
{
    public EventProjectionTests()
    {

    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(User));
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    protected void UseProjection<T>() where T : EventProjection, new()
    {
        StoreOptions(x => x.Projections.Add(new T(), ProjectionLifecycle.Inline));
    }

    [Fact]
    public void documents_created_by_event_projection_should_be_registered_as_document_types()
    {
        UseProjection<SimpleTransformProjectionUsingMetadata>();

        // MyAggregate is the aggregate type for AllGood above
        theStore.StorageFeatures.AllDocumentMappings.Select(x => x.DocumentType)
            .ShouldContain(typeof(User));
    }

    [Fact]
    public async Task use_simple_synchronous_project_methods()
    {
        UseProjection<SimpleProjection>();

        var stream = Guid.NewGuid();
        theSession.Events.StartStream(stream, new UserCreated { UserName = "one" },
            new UserCreated { UserName = "two" });

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList()
            .ShouldHaveTheSameElementsAs("one", "two");

        theSession.Events.Append(stream, new UserDeleted { UserName = "one" });
        await theSession.SaveChangesAsync();

        query.Query<User>()
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .Single()
            .ShouldBe("two");
    }

    [Fact]
    public async Task use_simple_synchronous_project_methods_with_inline_lambdas()
    {
        UseProjection<LambdaProjection>();

        var stream = Guid.NewGuid();
        theSession.Events.StartStream(stream, new UserCreated { UserName = "one" },
            new UserCreated { UserName = "two" });

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList()
            .ShouldHaveTheSameElementsAs("one", "two");

        theSession.Events.Append(stream, new UserDeleted { UserName = "one" });
        await theSession.SaveChangesAsync();

        query.Query<User>()
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .Single()
            .ShouldBe("two");
    }

    [Fact]
    public async Task synchronous_create_method()
    {
        UseProjection<SimpleCreatorProjection>();

        var stream = Guid.NewGuid();
        theSession.Events.StartStream(stream, new UserCreated { UserName = "one" },
            new UserCreated { UserName = "two" });

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList()
            .ShouldHaveTheSameElementsAs("one", "two");

        theSession.Events.Append(stream, new UserDeleted { UserName = "one" });
        await theSession.SaveChangesAsync();

        query.Query<User>()
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .Single()
            .ShouldBe("two");
    }

    [Fact]
    public async Task use_event_metadata()
    {
        UseProjection<SimpleTransformProjectionUsingMetadata>();

        var stream = Guid.NewGuid();
        theSession.Events.Append(stream, new UserCreated { UserName = "one" },
            new UserCreated { UserName = "two" });

        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task use_event_metadata_with_string_stream_identity()
    {
        StoreOptions(x =>
        {
            x.Events.StreamIdentity = StreamIdentity.AsString;
            x.Projections.Add(new SimpleTransformProjectionUsingMetadata(), ProjectionLifecycle.Inline);
        });

        var stream = Guid.NewGuid().ToString();
        theSession.Events.StartStream(stream, new UserCreated { UserName = "one" },
            new UserCreated { UserName = "two" });

        await theSession.SaveChangesAsync();

        theSession.Events.Append(stream, new UserCreated { UserName = "three" });
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task synchronous_with_transform_method()
    {
        UseProjection<SimpleTransformProjection>();

        var stream = Guid.NewGuid();
        theSession.Events.StartStream(stream, new UserCreated { UserName = "one" },
            new UserCreated { UserName = "two" });

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList()
            .ShouldHaveTheSameElementsAs("one", "two");

        theSession.Events.Append(stream, new UserDeleted { UserName = "one" });
        await theSession.SaveChangesAsync();

        query.Query<User>()
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .Single()
            .ShouldBe("two");
    }

    [Fact]
    public void empty_projection_throws_validation_error()
    {
        var projection = new EmptyProjection();
        Should.Throw<InvalidProjectionException>(() =>
        {
            projection.AssembleAndAssertValidity();
        });
    }
}

public class EmptyProjection: EventProjection
{
}

public class SimpleProjection: EventProjection
{
    public void Project(UserCreated @event, IDocumentOperations operations) =>
        operations.Store(new User { UserName = @event.UserName });

    public void Project(UserDeleted @event, IDocumentOperations operations) =>
        operations.DeleteWhere<User>(x => x.UserName == @event.UserName);
}

public class SimpleTransformProjection: EventProjection
{
    public User Transform(UserCreated @event) =>
        new User { UserName = @event.UserName };

    public void Project(UserDeleted @event, IDocumentOperations operations) =>
        operations.DeleteWhere<User>(x => x.UserName == @event.UserName);
}

public class OtherCreationEvent: UserCreated
{
}

public class SimpleTransformProjectionUsingMetadata: EventProjection
{
    public User Transform(IEvent<UserCreated> @event)
    {
        if (@event.StreamId == Guid.Empty && @event.StreamKey.IsEmpty())
        {
            throw new Exception("StreamKey and StreamId are both missing");
        }

        return new User { UserName = @event.Data.UserName };
    }

    public User Transform(IEvent<OtherCreationEvent> @event)
    {
        if (@event.StreamId == Guid.Empty && @event.StreamKey.IsEmpty())
        {
            throw new Exception("StreamKey and StreamId are both missing");
        }

        return new User { UserName = @event.Data.UserName };
    }

    public void Project(UserDeleted @event, IDocumentOperations operations) =>
        operations.DeleteWhere<User>(x => x.UserName == @event.UserName);
}

public class SimpleCreatorProjection: EventProjection
{
    public User Create(UserCreated e) => new User { UserName = e.UserName };

    public void Project(UserDeleted @event, IDocumentOperations operations) =>
        operations.DeleteWhere<User>(x => x.UserName == @event.UserName);
}

#region sample_lambda_definition_of_event_projection

public class LambdaProjection: EventProjection
{
    public LambdaProjection()
    {
        Project<UserCreated>((e, ops) =>
            ops.Store(new User { UserName = e.UserName }));

        Project<UserDeleted>((e, ops) =>
            ops.DeleteWhere<User>(x => x.UserName == e.UserName));
    }
}

#endregion

public class UserCreated
{
    public string UserName { get; set; }
}

public class UserDeleted
{
    public string UserName { get; set; }
}
