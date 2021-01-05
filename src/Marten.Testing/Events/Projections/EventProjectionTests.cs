using System;
using System.Linq;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    [Collection("event_projections")]
    public class EventProjectionTests : OneOffConfigurationsContext
    {
        public EventProjectionTests() : base("event_projections")
        {
            theStore.Advanced.Clean.DeleteDocumentsFor(typeof(User));
        }

        protected void UseProjection<T>() where T : EventProjection, new()
        {
            StoreOptions(x => x.Events.Projections.Inline(new T()));
        }

        [Fact]
        public void use_simple_synchronous_project_methods()
        {
            UseProjection<SimpleProjection>();

            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new UserCreated {UserName = "one"},
                new UserCreated {UserName = "two"});

            theSession.SaveChanges();

            using var query = theStore.QuerySession();

            query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("one", "two");

            theSession.Events.Append(stream, new UserDeleted {UserName = "one"});
            theSession.SaveChanges();

            query.Query<User>()
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .Single()
                .ShouldBe("two");
        }

        [Fact]
        public void use_simple_synchronous_project_methods_with_inline_lambdas()
        {
            UseProjection<SimpleProjection>();

            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new UserCreated {UserName = "one"},
                new UserCreated {UserName = "two"});

            theSession.SaveChanges();

            using var query = theStore.QuerySession();

            query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("one", "two");

            theSession.Events.Append(stream, new UserDeleted {UserName = "one"});
            theSession.SaveChanges();

            query.Query<User>()
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .Single()
                .ShouldBe("two");
        }

        [Fact]
        public void synchronous_create_method()
        {
            UseProjection<SimpleCreatorProjection>();

            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new UserCreated {UserName = "one"},
                new UserCreated {UserName = "two"});

            theSession.SaveChanges();

            using var query = theStore.QuerySession();

            query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("one", "two");

            theSession.Events.Append(stream, new UserDeleted {UserName = "one"});
            theSession.SaveChanges();

            query.Query<User>()
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .Single()
                .ShouldBe("two");
        }

        [Fact]
        public void synchronous_with_transform_method()
        {
            UseProjection<SimpleTransformProjection>();

            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new UserCreated {UserName = "one"},
                new UserCreated {UserName = "two"});

            theSession.SaveChanges();

            using var query = theStore.QuerySession();

            query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("one", "two");

            theSession.Events.Append(stream, new UserDeleted {UserName = "one"});
            theSession.SaveChanges();

            query.Query<User>()
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .Single()
                .ShouldBe("two");
        }
    }

    public class SimpleProjection: EventProjection
    {
        public void Project(UserCreated @event, IDocumentOperations operations) =>
            operations.Store(new User{UserName = @event.UserName});

        public void Project(UserDeleted @event, IDocumentOperations operations) =>
            operations.DeleteWhere<User>(x => x.UserName == @event.UserName);
    }

    public class SimpleTransformProjection: EventProjection
    {
        public User Transform(UserCreated @event) =>
            new User{UserName = @event.UserName};

        public void Project(UserDeleted @event, IDocumentOperations operations) =>
            operations.DeleteWhere<User>(x => x.UserName == @event.UserName);
    }

    public class SimpleCreatorProjection: EventProjection
    {
        public User Create(UserCreated e) => new User {UserName = e.UserName};

        public void Project(UserDeleted @event, IDocumentOperations operations) =>
            operations.DeleteWhere<User>(x => x.UserName == @event.UserName);
    }

    public class LambdaProjection: EventProjection
    {
        public LambdaProjection()
        {
            Project<UserCreated>((e, ops) =>
                ops.Store(new User {UserName = e.UserName}));

            Project<UserDeleted>((e, ops) =>
                ops.DeleteWhere<User>(x => x.UserName == e.UserName));
        }
    }

    public class UserCreated
    {
        public string UserName { get; set; }
    }

    public class UserDeleted
    {
        public string UserName { get; set; }
    }





}
