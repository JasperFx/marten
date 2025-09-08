using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3947_document_version_not_updating: IntegrationContext
{

    public Bug_3947_document_version_not_updating(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    [Fact]
    public async Task ensure_document_version_updates()
    {
        StoreOptions(options =>
        {
            options.AutoCreateSchemaObjects = AutoCreate.All;

            options.Events.AddEventType<UserCreated>();
            options.Events.AddEventType<UserGeneralUpdated>();
            options.Events.AddEventType<UserLoggedIn>();
            options.Events.AddEventType<UserProfilePicUpdated>();

            options.Schema.For<UserGeneral>();
            options.Projections.Add<UserGeneralProjection>(ProjectionLifecycle.Inline);

            options.Schema.For<UserSummary>();
            options.Projections.Add<UserSummaryProjection>(ProjectionLifecycle.Inline);
        });

        var userId = Guid.NewGuid();
        var user = new UserRecordReference(userId, "testuser", "secret");

        using (var session = theStore.LightweightSession())
        {
            var created = new UserCreated(user);

            session.Events.StartStream<User>(userId, created);
            await session.SaveChangesAsync();
        }

        long actualStreamVersion;

        using (var session = theStore.LightweightSession())
        {
            var updated = new UserProfilePicUpdated(user, "somebase64");

            session.Events.Append(userId, 2, updated);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            var loggedIn = new UserLoggedIn(user);

            var stream = session.Events.Append(userId, 3, loggedIn);
            await session.SaveChangesAsync();
            actualStreamVersion = stream.Version;
        }

        using (var querySession = theStore.QuerySession())
        {
            var userGeneral = querySession.Query<UserGeneral>().First();
            var userSummary = querySession.Query<UserSummary>().First();

            Assert.Equal(actualStreamVersion, userGeneral.Version);
            Assert.Equal(actualStreamVersion, userSummary.Version);
        }
    }

    public class User
    {
        public Guid Id { get; set; }
    }

    public class UserGeneral: IRevisioned
    {
        public required Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string ProfilePicBase64 { get; set; }
        public int Version { get; set; }
    }

    public class UserSummary: IRevisioned
    {
        public required Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public int Version { get; set; }
    }

    public record UserRecordReference(Guid Id, string FirstName, string LastName);
    public record UserCreated(UserRecordReference User);
    public record UserGeneralUpdated(UserRecordReference User, string? Email, string? FirstName, string? LastName);
    public record UserLoggedIn(UserRecordReference User);
    public record UserProfilePicUpdated(UserRecordReference User, string ProfilePicBase64);

    public class UserGeneralProjection: SingleStreamProjection<UserGeneral, Guid>
    {
        public static UserGeneral Create(UserCreated @event)
        {
            return new UserGeneral()
            {
                Id = @event.User.Id,
                FirstName = @event.User.FirstName,
                LastName = @event.User.LastName,
                ProfilePicBase64 = string.Empty
            };
        }

        public static void Apply(UserGeneral userGeneral, UserGeneralUpdated @event)
        {
            if (!string.IsNullOrEmpty(@event.FirstName))
            {
                userGeneral.FirstName = @event.FirstName;
            }

            if (!string.IsNullOrEmpty(@event.LastName))
            {
                userGeneral.LastName = @event.LastName;
            }
        }

        public static void Apply(UserGeneral userGeneral, UserProfilePicUpdated @event)
        {
            userGeneral.ProfilePicBase64 = @event.ProfilePicBase64;
        }
    }

    public class UserSummaryProjection: SingleStreamProjection<UserSummary, Guid>
    {
        public static UserSummary Create(UserCreated @event)
        {
            return new UserSummary()
            {
                Id = @event.User.Id,
                FirstName = @event.User.FirstName,
                LastName = @event.User.LastName,
            };
        }

        public static void Apply(UserSummary userSummary, UserGeneralUpdated @event)
        {
            if (!string.IsNullOrEmpty(@event.FirstName))
            {
                userSummary.FirstName = @event.FirstName;
            }

            if (!string.IsNullOrEmpty(@event.LastName))
            {
                userSummary.LastName = @event.LastName;
            }
        }
    }

}
