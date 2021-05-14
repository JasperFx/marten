using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Projections
{
    // License events
    public class LicenseCreated
    {
        public Guid LicenseId { get; }

        public string Name { get; }

        public LicenseCreated(Guid licenseId, string name)
        {
            LicenseId = licenseId;
            Name = name;
        }
    }

    public class LicenseFeatureToggled
    {
        public Guid LicenseId { get; }

        public string FeatureToggleName { get; }

        public LicenseFeatureToggled(Guid licenseId, string featureToggleName)
        {
            LicenseId = licenseId;
            FeatureToggleName = featureToggleName;
        }
    }

    // User Groups events

    public class UserGroupCreated
    {
        public Guid GroupId { get; }

        public string Name { get; }

        public UserGroupCreated(Guid groupId, string name)
        {
            GroupId = groupId;
            Name = name;
        }
    }

    public class UsersAssignedToGroup
    {
        public Guid GroupId { get; }

        public List<Guid> UserIds { get; }

        public UsersAssignedToGroup(Guid groupId, List<Guid> userIds)
        {
            GroupId = groupId;
            UserIds = userIds;
        }
    }

    // User Events
    public class UserRegistered
    {
        public Guid UserId { get; }

        public string Email { get; }

        public UserRegistered(Guid userId, string email)
        {
            UserId = userId;
            Email = email;
        }
    }

    public class UserLicenseAssigned
    {
        public Guid UserId { get; }

        public Guid LicenseId { get; }

        public UserLicenseAssigned(Guid userId, Guid licenseId)
        {
            UserId = userId;
            LicenseId = licenseId;
        }
    }

    public class UserFeatureToggles
    {
        public Guid Id { get; set; }

        public Guid LicenseId { get; set; }

        public List<string> FeatureToggles { get; } = new();
    }

    public class LicenseFeatureToggledEventGrouper : IAggregateGrouper<Guid>
    {
        public async Task Group(IQuerySession session, IEnumerable<IEvent> events, ITenantSliceGroup<Guid> grouping)
        {
            var licenceFeatureTogglesEvents = events
                .OfType<Event<LicenseFeatureToggled>>()
                .ToList();

            if (!licenceFeatureTogglesEvents.Any())
            {
                return;
            }

            // TODO -- let's build more samples first, but see if there's a useful
            // pattern for the next 3/4 operations later
            var licenceIds = licenceFeatureTogglesEvents
                .Select(e => e.Data.LicenseId)
                .ToList();

            var result = await session.Query<UserFeatureToggles>()
                .Where(x => licenceIds.Contains(x.LicenseId))
                .Select(x => new {x.Id, x.LicenseId})
                .ToListAsync();

            var streamIds = (IDictionary<Guid, List<Guid>>) result.GroupBy(ks => ks.LicenseId, vs=> vs.Id)
                .ToDictionary(ks=>ks.Key, vs => vs.ToList());

            grouping.AddEvents<LicenseFeatureToggled>(e => streamIds[e.LicenseId], licenceFeatureTogglesEvents);
        }
    }

    // projection with documentsession
    public class UserFeatureTogglesProjection: ViewProjection<UserFeatureToggles, Guid>
    {
        public UserFeatureTogglesProjection()
        {
            Identity<UserRegistered>(@event => @event.UserId);
            Identity<UserLicenseAssigned>(@event => @event.UserId);


            CustomGrouping(new LicenseFeatureToggledEventGrouper());
        }

        public void Apply(UserRegistered @event, UserFeatureToggles view)
        {
            view.Id = @event.UserId;
        }

        public void Apply(UserLicenseAssigned @event, UserFeatureToggles view)
        {
            view.LicenseId = @event.LicenseId;
        }

        public void Apply(LicenseFeatureToggled @event, UserFeatureToggles view)
        {
            view.FeatureToggles.Add(@event.FeatureToggleName);
        }
    }

    public class UserGroupsAssignment
    {
        public Guid Id { get; set; }

        public List<Guid> Groups { get; } = new();
    }

    public class UserGroupsAssignmentProjection: ViewProjection<UserGroupsAssignment, Guid>
    {
        public class CustomSlicer: IEventSlicer<UserGroupsAssignment, Guid>
        {
            public ValueTask<IReadOnlyList<EventSlice<UserGroupsAssignment, Guid>>> SliceInlineActions(IQuerySession querySession, IEnumerable<StreamAction> streams, ITenancy tenancy)
            {
                var allEvents = streams.SelectMany(x => x.Events).ToList();
                var group = new TenantSliceGroup<UserGroupsAssignment, Guid>(tenancy.Default);
                group.AddEvents<UserRegistered>(@event => @event.UserId, allEvents);
                group.AddEvents<UsersAssignedToGroup>(@event => @event.UserIds, allEvents);

                return new(group.Slices.ToList());
            }

            public ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupsAssignment, Guid>>> SliceAsyncEvents(IQuerySession querySession, List<IEvent> events, ITenancy tenancy)
            {
                var group = new TenantSliceGroup<UserGroupsAssignment, Guid>(tenancy.Default);
                group.AddEvents<UserRegistered>(@event => @event.UserId, events);
                group.AddEvents<UsersAssignedToGroup>(@event => @event.UserIds, events);

                return new(new List<TenantSliceGroup<UserGroupsAssignment, Guid>>{group});
            }
        }


        public UserGroupsAssignmentProjection()
        {
            CustomGrouping(new CustomSlicer());
        }

        public void Apply(UserRegistered @event, UserGroupsAssignment view)
        {
            view.Id = @event.UserId;
        }

        public void Apply(UsersAssignedToGroup @event, UserGroupsAssignment view)
        {
            view.Groups.Add(@event.GroupId);
        }
    }



    public class multiple_streams_projections: IntegrationContext
    {
        [Fact]
        public async Task multi_stream_projections_should_work()
        {
            // --------------------------------
            // Create Licenses
            // --------------------------------
            // Free License
            // Premium License
            // --------------------------------

            var freeLicenseCreated = new LicenseCreated(Guid.NewGuid(), "Free Licence");
            theSession.Events.Append(freeLicenseCreated.LicenseId, freeLicenseCreated);

            var premiumLicenseCreated = new LicenseCreated(Guid.NewGuid(), "Premium Licence");
            theSession.Events.Append(premiumLicenseCreated.LicenseId, premiumLicenseCreated);

            await theSession.SaveChangesAsync();

            // --------------------------------
            // Create Groups
            // --------------------------------
            // Regular Users
            // Admin Users
            // --------------------------------

            var regularUsersGroupCreated = new UserGroupCreated(Guid.NewGuid(), "Regular Users");
            theSession.Events.Append(regularUsersGroupCreated.GroupId, regularUsersGroupCreated);

            var adminUsersGroupCreated = new UserGroupCreated(Guid.NewGuid(), "Admin Users");
            theSession.Events.Append(adminUsersGroupCreated.GroupId, adminUsersGroupCreated);

            await theSession.SaveChangesAsync();

            // --------------------------------
            // Create Users
            // --------------------------------
            // Anna
            // John
            // Maggie
            // Alan
            // --------------------------------

            var annaRegistered = new UserRegistered(Guid.NewGuid(), "Anna");
            theSession.Events.Append(annaRegistered.UserId, annaRegistered);

            var johnRegistered = new UserRegistered(Guid.NewGuid(), "John");
            theSession.Events.Append(johnRegistered.UserId, johnRegistered);

            var maggieRegistered = new UserRegistered(Guid.NewGuid(), "Maggie");
            theSession.Events.Append(maggieRegistered.UserId, maggieRegistered);

            var alanRegistered = new UserRegistered(Guid.NewGuid(), "Alan");
            theSession.Events.Append(alanRegistered.UserId, alanRegistered);

            await theSession.SaveChangesAsync();

            // --------------------------------
            // Assign users' licences
            // --------------------------------
            // Anna, Maggie => Premium
            // John, Alan   => Free
            // --------------------------------

            var annaAssignedToPremiumLicense =
                new UserLicenseAssigned(annaRegistered.UserId, premiumLicenseCreated.LicenseId);
            theSession.Events.Append(annaRegistered.UserId, annaAssignedToPremiumLicense);

            var johnAssignedToFreeLicense =
                new UserLicenseAssigned(johnRegistered.UserId, freeLicenseCreated.LicenseId);
            theSession.Events.Append(johnRegistered.UserId, johnAssignedToFreeLicense);

            var maggieAssignedToPremiumLicense =
                new UserLicenseAssigned(maggieRegistered.UserId, premiumLicenseCreated.LicenseId);
            theSession.Events.Append(maggieAssignedToPremiumLicense.UserId, maggieAssignedToPremiumLicense);

            var alanAssignedToFreeLicense =
                new UserLicenseAssigned(alanRegistered.UserId, freeLicenseCreated.LicenseId);
            theSession.Events.Append(alanAssignedToFreeLicense.UserId, alanAssignedToFreeLicense);

            await theSession.SaveChangesAsync();

            // --------------------------------
            // Assign feature toggle to license
            // --------------------------------
            // Login  => Free, Premium
            // Invite => Premium
            // --------------------------------

            var loginFeatureToggle = "Login";
            var loginFeatureToggledOnFreeLicense =
                new LicenseFeatureToggled(freeLicenseCreated.LicenseId, loginFeatureToggle);
            theSession.Events.Append(loginFeatureToggledOnFreeLicense.LicenseId, loginFeatureToggledOnFreeLicense);

            var loginFeatureToggledOnPremiumLicense =
                new LicenseFeatureToggled(premiumLicenseCreated.LicenseId, loginFeatureToggle);
            theSession.Events.Append(loginFeatureToggledOnPremiumLicense.LicenseId,
                loginFeatureToggledOnPremiumLicense);

            var inviteFeatureToggle = "Invite";
            var inviteToggledOnPremiumLicense =
                new LicenseFeatureToggled(premiumLicenseCreated.LicenseId, inviteFeatureToggle);
            theSession.Events.Append(inviteToggledOnPremiumLicense.LicenseId, inviteToggledOnPremiumLicense);

            await theSession.SaveChangesAsync();

            // --------------------------------
            // Check users' feature toggles
            // --------------------------------
            // Anna, Maggie => Premium => Login, Invite
            // John, Alan   => Free    => Login
            // --------------------------------

            var annaFeatureToggles = await theSession.LoadAsync<UserFeatureToggles>(annaRegistered.UserId);
            annaFeatureToggles.ShouldNotBeNull();
            annaFeatureToggles.Id.ShouldBe(annaRegistered.UserId);
            annaFeatureToggles.LicenseId.ShouldBe(premiumLicenseCreated.LicenseId);
            annaFeatureToggles.FeatureToggles.ShouldHaveTheSameElementsAs(loginFeatureToggle, inviteFeatureToggle);

            var maggieFeatureToggles = await theSession.LoadAsync<UserFeatureToggles>(maggieRegistered.UserId);
            maggieFeatureToggles.ShouldNotBeNull();
            maggieFeatureToggles.Id.ShouldBe(maggieRegistered.UserId);
            maggieFeatureToggles.LicenseId.ShouldBe(premiumLicenseCreated.LicenseId);
            maggieFeatureToggles.FeatureToggles.ShouldHaveTheSameElementsAs(loginFeatureToggle, inviteFeatureToggle);

            var johnFeatureToggles = await theSession.LoadAsync<UserFeatureToggles>(johnRegistered.UserId);
            johnFeatureToggles.ShouldNotBeNull();
            johnFeatureToggles.Id.ShouldBe(johnRegistered.UserId);
            johnFeatureToggles.LicenseId.ShouldBe(freeLicenseCreated.LicenseId);
            johnFeatureToggles.FeatureToggles.ShouldHaveTheSameElementsAs(loginFeatureToggle);

            var alanFeatureToggles = await theSession.LoadAsync<UserFeatureToggles>(alanRegistered.UserId);
            alanFeatureToggles.ShouldNotBeNull();
            alanFeatureToggles.Id.ShouldBe(alanRegistered.UserId);
            alanFeatureToggles.LicenseId.ShouldBe(freeLicenseCreated.LicenseId);
            alanFeatureToggles.FeatureToggles.ShouldHaveTheSameElementsAs(loginFeatureToggle);

            // --------------------------------
            // Assign users to Groups
            // --------------------------------
            // Anna, Maggie => Admin
            // John, Alan   => Regular
            // --------------------------------

            var annaAndMaggieAssignedToAdminUsersGroup = new UsersAssignedToGroup(adminUsersGroupCreated.GroupId,
                new List<Guid> {annaRegistered.UserId, maggieRegistered.UserId});
            theSession.Events.Append(annaAndMaggieAssignedToAdminUsersGroup.GroupId,
                annaAndMaggieAssignedToAdminUsersGroup);

            var johnAndAlanAssignedToRegularUsersGroup = new UsersAssignedToGroup(regularUsersGroupCreated.GroupId,
                new List<Guid> {johnRegistered.UserId, alanRegistered.UserId});
            theSession.Events.Append(johnAndAlanAssignedToRegularUsersGroup.GroupId,
                johnAndAlanAssignedToRegularUsersGroup);

            await theSession.SaveChangesAsync();

            // --------------------------------
            // Check users' groups assignment
            // --------------------------------
            // Anna, Maggie => Admin
            // John, Alan   => Regular
            // --------------------------------

            var annaGroupAssignment = await theSession.LoadAsync<UserGroupsAssignment>(annaRegistered.UserId);
            annaGroupAssignment.ShouldNotBeNull();
            annaGroupAssignment.Id.ShouldBe(annaRegistered.UserId);
            annaGroupAssignment.Groups.ShouldHaveTheSameElementsAs(adminUsersGroupCreated.GroupId);

            var maggieGroupAssignment = await theSession.LoadAsync<UserGroupsAssignment>(maggieRegistered.UserId);
            maggieGroupAssignment.ShouldNotBeNull();
            maggieGroupAssignment.Id.ShouldBe(maggieRegistered.UserId);
            maggieGroupAssignment.Groups.ShouldHaveTheSameElementsAs(adminUsersGroupCreated.GroupId);

            var johnGroupAssignment = await theSession.LoadAsync<UserGroupsAssignment>(johnRegistered.UserId);
            johnGroupAssignment.ShouldNotBeNull();
            johnGroupAssignment.Id.ShouldBe(johnRegistered.UserId);
            johnGroupAssignment.Groups.ShouldHaveTheSameElementsAs(regularUsersGroupCreated.GroupId);

            var alanGroupAssignment = await theSession.LoadAsync<UserGroupsAssignment>(alanRegistered.UserId);
            alanGroupAssignment.ShouldNotBeNull();
            alanGroupAssignment.Id.ShouldBe(alanRegistered.UserId);
            alanGroupAssignment.Groups.ShouldHaveTheSameElementsAs(regularUsersGroupCreated.GroupId);
        }

        public multiple_streams_projections(DefaultStoreFixture fixture): base(fixture)
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.DatabaseSchemaName = "multi_stream_projections";

                _.Projections.Add<UserFeatureTogglesProjection>(ProjectionLifecycle.Inline);
                _.Projections.Add<UserGroupsAssignmentProjection>(ProjectionLifecycle.Inline);
            });
        }
    }
}
