using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.ViewProjections.SimpleWithOneToMany
{
    public class UserSocialCircleProjection: ViewProjection<UserSocialCircle, Guid>
    {
        public UserSocialCircleProjection()
        {
            Identity<UserRegistered>(x => x.UserId);

            // Multiple Identity methods can be called for the same event
            // Useful for describing different projection mappings
            Identity<UserFollowsUsers>();
            Identities<UserFollowsUsers>(x => x.FollowingUsers);
        }

        public void Apply(UserRegistered @event, UserSocialCircle view)
        {
            view.Id = @event.UserId;
        }

        public void Apply(UserFollowsUsers @event, UserSocialCircle view)
        {
            if (@event.UserId == view.Id)
            {
                view.FollowingUsers.AddRange(@event.FollowingUsers.Where(user => !view.FollowingUsers.Contains(user)));
            }

            if (@event.FollowingUsers.Contains(view.Id) && !view.FollowedByUsers.Contains(@event.UserId))
            {
                view.FollowedByUsers.Add(@event.UserId);
            }
        }
    }
    

    public class simple_multi_stream_projection_with_one_to_many_using_self_aggregate: OneOffConfigurationsContext
    {
        [Fact]
        public async Task multi_stream_projections_should_work()
        {
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
            // Follow others
            // --------------------------------
            // Anna   => John, Alan
            // John   => Anna
            // Maggie => Anna, Alan
            // --------------------------------

            var annaFollowsOthers = new UserFollowsUsers(annaRegistered.UserId,
                new[] { johnRegistered.UserId, alanRegistered.UserId });
            theSession.Events.Append(annaFollowsOthers.UserId, annaFollowsOthers);

            var johnFollowsOthers = new UserFollowsUsers(johnRegistered.UserId,
                new[] { annaRegistered.UserId });
            theSession.Events.Append(johnFollowsOthers.UserId, johnFollowsOthers);

            var maggieFollowsOthers = new UserFollowsUsers(maggieRegistered.UserId,
                new[] {annaRegistered.UserId, alanRegistered.UserId});
            theSession.Events.Append(maggieFollowsOthers.UserId, maggieFollowsOthers);

            await theSession.SaveChangesAsync();

            // --------------------------------
            // Check social circles
            // ================================
            // Follows
            // --------------------------------
            // Anna   => John, Alan
            // John   => Anna
            // Maggie => Anna, Alan
            // Alan   => <none>
            // --------------------------------
            // Is followed by
            // --------------------------------
            // Anna   => John, Maggie
            // John   => Anna
            // Maggie => <none>
            // Alan   => Anna, Maggie
            // --------------------------------

            var annaSocialCircles = await theSession.LoadAsync<UserSocialCircle>(annaRegistered.UserId);
            annaSocialCircles.ShouldNotBeNull();
            annaSocialCircles.Id.ShouldBe(annaRegistered.UserId);
            annaSocialCircles.FollowingUsers.ShouldHaveTheSameElementsAs(johnRegistered.UserId, alanRegistered.UserId);
            annaSocialCircles.FollowedByUsers.ShouldHaveTheSameElementsAs(johnRegistered.UserId, maggieRegistered.UserId);

            var johnSocialCircles = await theSession.LoadAsync<UserSocialCircle>(johnRegistered.UserId);
            johnSocialCircles.ShouldNotBeNull();
            johnSocialCircles.Id.ShouldBe(johnRegistered.UserId);
            johnSocialCircles.FollowingUsers.ShouldHaveTheSameElementsAs(annaRegistered.UserId);
            johnSocialCircles.FollowedByUsers.ShouldHaveTheSameElementsAs(annaRegistered.UserId);

            var maggieSocialCircles = await theSession.LoadAsync<UserSocialCircle>(maggieRegistered.UserId);
            maggieSocialCircles.ShouldNotBeNull();
            maggieSocialCircles.Id.ShouldBe(maggieRegistered.UserId);
            maggieSocialCircles.FollowingUsers.ShouldHaveTheSameElementsAs(annaRegistered.UserId, alanRegistered.UserId);
            maggieSocialCircles.FollowedByUsers.ShouldBeEmpty();

            var alanSocialCircles = await theSession.LoadAsync<UserSocialCircle>(alanRegistered.UserId);
            alanSocialCircles.ShouldNotBeNull();
            alanSocialCircles.Id.ShouldBe(alanRegistered.UserId);
            alanSocialCircles.FollowingUsers.ShouldBeEmpty();
            alanSocialCircles.FollowedByUsers.ShouldHaveTheSameElementsAs(annaRegistered.UserId, maggieRegistered.UserId);
        }

        public simple_multi_stream_projection_with_one_to_many_using_self_aggregate()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "simple_multi_stream_projection_one_to_many_using_self_aggregate";
                _.Projections.Add<UserSocialCircleProjection>(ProjectionLifecycle.Inline);
            });
        }
    }
}
