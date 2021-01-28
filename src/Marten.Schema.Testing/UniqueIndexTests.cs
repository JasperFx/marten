using System;
using Marten.Events.Projections;
using Marten.Exceptions;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Schema.Testing
{

    public class UserCreated
    {
        public Guid UserId { get; set; }
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string Surname { get; set; }
    }

    public class UserViewProjection : ViewProjection<UniqueUser, Guid>
    {
        public UserViewProjection()
        {
            Identity<UserCreated>(x => x.UserId);
        }

        public void Apply(UniqueUser view, UserCreated @event)
        {
            view.Id = @event.UserId;
            view.Email = @event.Email;
            view.UserName = @event.Email;
            view.FirstName = @event.Email;
            view.Surname = @event.Surname;
        }
    }

    public class UniqueUser
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

    public class UniqueIndexTests : IntegrationContext
    {



        public const string UniqueSqlState = "23505";

        public UniqueIndexTests(ITestOutputHelper output = null) : base(output)
        {
            StoreOptions(opts =>
            {
                opts.Events.AddEventTypes(new[] { typeof(UserCreated) });
                opts.Events.Projections.Add(new UserViewProjection());
                opts.RegisterDocumentType<UniqueUser>();
            });
        }

        [Fact]
        public void given_two_documents_with_the_same_value_for_unique_field_with_multiple_properties_when_created_then_throws_exception()
        {
            //1. Create Events
            var firstDocument = new UniqueUser { Id = Guid.NewGuid(), Email = "john.doe@gmail.com", FirstName = "John", Surname = "Doe" };
            var secondDocument = new UniqueUser { Id = Guid.NewGuid(), Email = "some.mail@outlook.com", FirstName = "John", Surname = "Doe" };


            using (var session = theStore.OpenSession())
            {
                //2. Save documents
                session.Store(firstDocument);
                session.Store(secondDocument);

                //3. Unique Exception Was thrown
                try
                {
                    session.SaveChanges();
                }
                catch (DocumentAlreadyExistsException exception)
                {
                    ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                }
            }
        }

        [Fact]
        public void given_two_documents_with_the_same_value_for_unique_field_with_single_property_when_created_then_throws_exception()
        {
            //1. Create Events
            const string email = "john.smith@mail.com";
            var firstDocument = new UniqueUser { Id = Guid.NewGuid(), Email = email, FirstName = "John", Surname = "Smith" };
            var secondDocument = new UniqueUser { Id = Guid.NewGuid(), Email = email, FirstName = "John", Surname = "Doe" };

            using (var session = theStore.OpenSession())
            {
                //2. Save documents
                session.Store(firstDocument);
                session.Store(secondDocument);

                //3. Unique Exception Was thrown
                try
                {
                    session.SaveChanges();
                }
                catch (DocumentAlreadyExistsException exception)
                {
                    ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                }
            }
        }

        [Fact]
        public void given_two_events_with_the_same_value_for_unique_field_with_multiple_properties_when_inline_transformation_is_applied_then_throws_exception()
        {
            //1. Create Events
            var firstEvent = new UserCreated { UserId = Guid.NewGuid(), Email = "john.doe@gmail.com", FirstName = "John", Surname = "Doe" };
            var secondEvent = new UserCreated { UserId = Guid.NewGuid(), Email = "some.mail@outlook.com", FirstName = "John", Surname = "Doe" };

            using (var session = theStore.OpenSession())
            {
                //2. Publish Events
                session.Events.Append(firstEvent.UserId, firstEvent);
                session.Events.Append(secondEvent.UserId, secondEvent);

                //3. Unique Exception Was thrown
                try
                {
                    session.SaveChanges();
                }
                catch (Marten.Exceptions.MartenCommandException exception)
                {
                    ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
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

            using (var session = theStore.OpenSession())
            {
                //2. Publish Events
                session.Events.Append(firstEvent.UserId, firstEvent);
                session.Events.Append(secondEvent.UserId, secondEvent);

                //3. Unique Exception Was thrown
                try
                {
                    session.SaveChanges();
                }
                catch (DocumentAlreadyExistsException exception)
                {
                    ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                }
            }
        }

    }
}
