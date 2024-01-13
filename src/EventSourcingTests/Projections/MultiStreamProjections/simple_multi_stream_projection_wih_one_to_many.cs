using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.MultiStreamProjections.Samples
{
    #region sample_view-projection-simple-with-one-to-many

    public class UserGroupsAssignmentProjection: MultiStreamProjection<UserGroupsAssignment, Guid>
    {
        public UserGroupsAssignmentProjection()
        {
            Identity<UserRegistered>(x => x.UserId);
            Identities<MultipleUsersAssignedToGroup>(x => x.UserIds);
        }

        public void Apply(UserRegistered @event, UserGroupsAssignment view)
        {
            view.Id = @event.UserId;
        }

        public void Apply(MultipleUsersAssignedToGroup @event, UserGroupsAssignment view)
        {
            view.Groups.Add(@event.GroupId);
        }
    }

    #endregion
}

namespace EventSourcingTests.Projections.MultiStreamProjections
{


    public class simple_multi_stream_projection_with_one_to_many: OneOffConfigurationsContext
    {
        [Fact]
        public async Task multi_stream_projections_should_work()
        {

            // --------------------------------
            // Create Groups
            // --------------------------------
            // Regular Users
            // Admin Users
            // --------------------------------

            var regularUsersGroupCreated = new UserGroupCreated(Guid.NewGuid(), "Regular Users");
            TheSession.Events.Append(regularUsersGroupCreated.GroupId, regularUsersGroupCreated);

            var adminUsersGroupCreated = new UserGroupCreated(Guid.NewGuid(), "Admin Users");
            TheSession.Events.Append(adminUsersGroupCreated.GroupId, adminUsersGroupCreated);

            await TheSession.SaveChangesAsync();

            // --------------------------------
            // Create Users
            // --------------------------------
            // Anna
            // John
            // Maggie
            // Alan
            // --------------------------------

            var annaRegistered = new UserRegistered(Guid.NewGuid(), "Anna");
            TheSession.Events.Append(annaRegistered.UserId, annaRegistered);

            var johnRegistered = new UserRegistered(Guid.NewGuid(), "John");
            TheSession.Events.Append(johnRegistered.UserId, johnRegistered);

            var maggieRegistered = new UserRegistered(Guid.NewGuid(), "Maggie");
            TheSession.Events.Append(maggieRegistered.UserId, maggieRegistered);

            var alanRegistered = new UserRegistered(Guid.NewGuid(), "Alan");
            TheSession.Events.Append(alanRegistered.UserId, alanRegistered);

            await TheSession.SaveChangesAsync();

            // --------------------------------
            // Assign users to Groups
            // --------------------------------
            // Anna, Maggie => Admin
            // John, Alan   => Regular
            // --------------------------------

            var annaAndMaggieAssignedToAdminUsersGroup = new MultipleUsersAssignedToGroup(adminUsersGroupCreated.GroupId,
                new List<Guid> {annaRegistered.UserId, maggieRegistered.UserId});
            TheSession.Events.Append(annaAndMaggieAssignedToAdminUsersGroup.GroupId,
                annaAndMaggieAssignedToAdminUsersGroup);

            var johnAndAlanAssignedToRegularUsersGroup = new MultipleUsersAssignedToGroup(regularUsersGroupCreated.GroupId,
                new List<Guid> {johnRegistered.UserId, alanRegistered.UserId});
            TheSession.Events.Append(johnAndAlanAssignedToRegularUsersGroup.GroupId,
                johnAndAlanAssignedToRegularUsersGroup);

            await TheSession.SaveChangesAsync();

            // --------------------------------
            // Check users' groups assignment
            // --------------------------------
            // Anna, Maggie => Admin
            // John, Alan   => Regular
            // --------------------------------

            var annaGroupAssignment = await TheSession.LoadAsync<UserGroupsAssignment>(annaRegistered.UserId);
            annaGroupAssignment.ShouldNotBeNull();
            annaGroupAssignment.Id.ShouldBe(annaRegistered.UserId);
            annaGroupAssignment.Groups.ShouldHaveTheSameElementsAs(adminUsersGroupCreated.GroupId);

            var maggieGroupAssignment = await TheSession.LoadAsync<UserGroupsAssignment>(maggieRegistered.UserId);
            maggieGroupAssignment.ShouldNotBeNull();
            maggieGroupAssignment.Id.ShouldBe(maggieRegistered.UserId);
            maggieGroupAssignment.Groups.ShouldHaveTheSameElementsAs(adminUsersGroupCreated.GroupId);

            var johnGroupAssignment = await TheSession.LoadAsync<UserGroupsAssignment>(johnRegistered.UserId);
            johnGroupAssignment.ShouldNotBeNull();
            johnGroupAssignment.Id.ShouldBe(johnRegistered.UserId);
            johnGroupAssignment.Groups.ShouldHaveTheSameElementsAs(regularUsersGroupCreated.GroupId);

            var alanGroupAssignment = await TheSession.LoadAsync<UserGroupsAssignment>(alanRegistered.UserId);
            alanGroupAssignment.ShouldNotBeNull();
            alanGroupAssignment.Id.ShouldBe(alanRegistered.UserId);
            alanGroupAssignment.Groups.ShouldHaveTheSameElementsAs(regularUsersGroupCreated.GroupId);
        }

        public simple_multi_stream_projection_with_one_to_many()
        {
            StoreOptions(_ =>
            {
                _.Projections.Add<EventSourcingTests.Projections.MultiStreamProjections.Samples.UserGroupsAssignmentProjection>(ProjectionLifecycle.Inline);
            });
        }
    }
}
