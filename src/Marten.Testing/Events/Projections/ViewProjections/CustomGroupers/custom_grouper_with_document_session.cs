using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.ViewProjections.CustomGroupers
{
    #region sample_view-projection-custom-grouper-with-querysession
    public class LicenseFeatureToggledEventGrouper: IAggregateGrouper<Guid>
    {
        public async Task Group(IQuerySession session, IEnumerable<IEvent> events, ITenantSliceGroup<Guid> grouping)
        {
            var licenceFeatureTogglesEvents = events
                .OfType<IEvent<LicenseFeatureToggled>>()
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

            var streamIds = (IDictionary<Guid, List<Guid>>)result.GroupBy(ks => ks.LicenseId, vs => vs.Id)
                .ToDictionary(ks => ks.Key, vs => vs.ToList());

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

    #endregion

    public class custom_grouper_with_document_session: IntegrationContext
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
        }

        public custom_grouper_with_document_session(DefaultStoreFixture fixture): base(fixture)
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "custom_grouper_with_document_session";

                _.Projections.Add<UserFeatureTogglesProjection>(ProjectionLifecycle.Inline);
            });
        }
    }
}
