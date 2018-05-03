using System;
using Marten.Events.Projections;
using Marten.Schema;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class UniqueIndexTests
    {
        private class UserCreated
        {
            public Guid UserId { get; set; }
            public string Email { get; set; }
            public string FirstName { get; set; }
            public string Surname { get; set; }
        }

        private class User
        {
            public Guid Id { get; set; }

            public string UserName { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField)]
            public string Email { get; set; }

            [UniqueIndex(IndexName = "fullname")]
            public string FirstName { get; set; }

            [UniqueIndex(IndexName = "fullname")]
            public string Surname { get; set; }
        }

        private class UserViewProjection : ViewProjection<User, Guid>
        {
            public UserViewProjection()
            {
                ProjectEvent<UserCreated>(Apply);
            }

            private void Apply(User view, UserCreated @event)
            {
                view.Id = @event.UserId;
                view.Email = @event.Email;
                view.UserName = @event.Email;
                view.FirstName = @event.Email;
                view.Surname = @event.Surname;
            }
        }

        public IDocumentStore InitStore()
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Events.AddEventTypes(new[] { typeof(UserCreated) });
            options.Events.InlineProjections.Add(new UserViewProjection());
            options.RegisterDocumentType<User>();

            return DocumentStore.For(ConnectionSource.ConnectionString);
        }

        public const string UniqueSqlState = "23505";

        [Fact]
        public void given_two_documents_with_the_same_value_for_unique_field_with_multiple_properties_when_created_then_throws_exception()
        {
            //1. Create Events
            var firstDocument = new User { Id = Guid.NewGuid(), Email = "john.doe@gmail.com", FirstName = "John", Surname = "Doe" };
            var secondDocument = new User { Id = Guid.NewGuid(), Email = "some.mail@outlook.com", FirstName = "John", Surname = "Doe" };

            using (var store = InitStore())
            {
                using (var session = store.OpenSession())
                {
                    //2. Save documents
                    session.Store(firstDocument);
                    session.Store(secondDocument);

                    //3. Unique Exception Was thrown
                    try
                    {
                        session.SaveChanges();
                    }
                    catch (MartenCommandException exception)
                    {
                        ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                    }
                }
            }
        }

        [Fact]
        public void given_two_documents_with_the_same_value_for_unique_field_with_single_property_when_created_then_throws_exception()
        {
            //1. Create Events
            const string email = "john.smith@mail.com";
            var firstDocument = new User { Id = Guid.NewGuid(), Email = email, FirstName = "John", Surname = "Smith" };
            var secondDocument = new User { Id = Guid.NewGuid(), Email = email, FirstName = "John", Surname = "Doe" };

            using (var store = InitStore())
            {
                using (var session = store.OpenSession())
                {
                    //2. Save documents
                    session.Store(firstDocument);
                    session.Store(secondDocument);

                    //3. Unique Exception Was thrown
                    try
                    {
                        session.SaveChanges();
                    }
                    catch (MartenCommandException exception)
                    {
                        ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                    }
                }
            }
        }

        [Fact]
        public void given_two_events_with_the_same_value_for_unique_field_with_multiple_properties_when_inline_transformation_is_applied_then_throws_exception()
        {
            //1. Create Events
            var firstEvent = new UserCreated { UserId = Guid.NewGuid(), Email = "john.doe@gmail.com", FirstName = "John", Surname = "Doe" };
            var secondEvent = new UserCreated { UserId = Guid.NewGuid(), Email = "some.mail@outlook.com", FirstName = "John", Surname = "Doe" };

            using (var store = InitStore())
            {
                using (var session = store.OpenSession())
                {
                    //2. Publish Events
                    session.Events.Append(firstEvent.UserId, firstEvent);
                    session.Events.Append(secondEvent.UserId, secondEvent);

                    //3. Unique Exception Was thrown
                    try
                    {
                        session.SaveChanges();
                    }
                    catch (MartenCommandException exception)
                    {
                        ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                    }
                }
            }
        }

        [Fact]
        public void given_two_events_with_the_same_value_for_unique_field_with_single_property_when_inline_transformation_is_applied_then_throws_exception()
        {
            //1. Create Events
            const string email = "john.smith@mail.com";
            var firstEvent = new UserCreated { UserId = Guid.NewGuid(), Email = email, FirstName = "John", Surname = "Smith" };
            var secondEvent = new UserCreated { UserId = Guid.NewGuid(), Email = email, FirstName = "John", Surname = "Doe" };

            using (var store = InitStore())
            {
                using (var session = store.OpenSession())
                {
                    //2. Publish Events
                    session.Events.Append(firstEvent.UserId, firstEvent);
                    session.Events.Append(secondEvent.UserId, secondEvent);

                    //3. Unique Exception Was thrown
                    try
                    {
                        session.SaveChanges();
                    }
                    catch (MartenCommandException exception)
                    {
                        ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                    }
                }
            }
        }
    }
}