﻿using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.MultiStreamProjections;

#region sample_view-projection-simple
public class UserGroupsAssignmentProjection: MultiStreamProjection<UserGroupsAssignment, Guid>
{
    public UserGroupsAssignmentProjection()
    {
        // This is just specifying the aggregate document id
        // per event type. This assumes that each event
        // applies to only one aggregated view document
        Identity<UserRegistered>(x => x.UserId);
        Identity<SingleUserAssignedToGroup>(x => x.UserId);
    }

    public void Apply(UserRegistered @event, UserGroupsAssignment view)
        => view.Id = @event.UserId;

    public void Apply(SingleUserAssignedToGroup @event, UserGroupsAssignment view)
        => view.Groups.Add(@event.GroupId);
}

#endregion

#region sample_view-projection-simple-2
public class UserGroupsAssignmentProjection2: MultiStreamProjection<UserGroupsAssignment, Guid>
{
    public UserGroupsAssignmentProjection2()
    {
        // This is just specifying the aggregate document id
        // per event type. This assumes that each event
        // applies to only one aggregated view document

        // The easiest possible way to do this is to use
        // a common interface or base type, and specify
        // the identity rule on that common type
        Identity<IUserEvent>(x => x.UserId);
    }

    public void Apply(UserRegistered @event, UserGroupsAssignment view)
        => view.Id = @event.UserId;

    public void Apply(SingleUserAssignedToGroup @event, UserGroupsAssignment view)
        => view.Groups.Add(@event.GroupId);
}

#endregion

public class simple_multi_stream_projection: IntegrationContext
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
        // Assign users to Groups
        // --------------------------------
        // Anna, Maggie => Admin
        // John, Alan   => Regular
        // --------------------------------

        var annaAssignedToAdminUsersGroup = new SingleUserAssignedToGroup(adminUsersGroupCreated.GroupId,
            annaRegistered.UserId);

        var maggieAssignedToAdminUsersGroup = new SingleUserAssignedToGroup(adminUsersGroupCreated.GroupId,
            maggieRegistered.UserId);
        theSession.Events.Append(
            adminUsersGroupCreated.GroupId,
            annaAssignedToAdminUsersGroup,
            maggieAssignedToAdminUsersGroup
        );

        var johnAssignedToRegularUsersGroup = new SingleUserAssignedToGroup(regularUsersGroupCreated.GroupId,
            johnRegistered.UserId);
        var alanAssignedToRegularUsersGroup = new SingleUserAssignedToGroup(regularUsersGroupCreated.GroupId,
            alanRegistered.UserId);
        theSession.Events.Append(
            regularUsersGroupCreated.GroupId,
            johnAssignedToRegularUsersGroup,
            alanAssignedToRegularUsersGroup
        );

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

    public simple_multi_stream_projection(DefaultStoreFixture fixture): base(fixture)
    {
        StoreOptions(_ =>
        {
            _.DatabaseSchemaName = "simple_multi_stream_projection";

            _.Projections.Add<UserGroupsAssignmentProjection>(ProjectionLifecycle.Inline);
        });
    }
}
