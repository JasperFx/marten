using System;
using JasperFx;
using Marten;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Sessions;
using Marten.Storage;
using NSubstitute;
using Shouldly;
using Xunit;

#nullable enable

namespace CoreTests.Sessions;

using static TenantedSessionFactoryTests.Configuration;

public class TenantedSessionFactoryTests
{
    // |---------------------------------------------------------|
    // | SCENARIOS                                               |
    // |---------------------------------------------------------|
    // | SESSION     | SLICE       | STORAGE   | RESULT          |
    // |-------------|-------------|-----------|-----------------|
    // | DEFAULT     | DEFAULT     | SINGLE    | THE SAME        |
    // | DEFAULT     | DEFAULT     | CONJOINED | THE SAME        |
    // | DEFAULT     | NON-DEFAULT | SINGLE    | THE SAME        |
    // | DEFAULT     | NON-DEFAULT | CONJOINED | NEW NON-DEFAULT |
    // | NON-DEFAULT | DEFAULT     | SINGLE    | NEW DEFAULT     |
    // | NON-DEFAULT | DEFAULT     | CONJOINED | THE SAME        |
    // | NON-DEFAULT | NON-DEFAULT | SINGLE    | NEW DEFAULT     |
    // | NON-DEFAULT | NON-DEFAULT | CONJOINED | THE SAME        |
    [Theory]
    [MemberData(nameof(Configurations))]
    public void Verify(Configuration setup)
    {
        var session = SessionWith(setup);

        var slice = SliceWith(setup);

        var storage = StorageWith(setup);

        var newSession = session.UseTenancyBasedOnSliceAndStorage(storage, slice);

        if (!setup.ExpectsNewSession)
        {
            newSession.ShouldBe(session);
        }
        else
        {
            newSession.ShouldNotBe(session);
            newSession.ShouldBeOfType<NestedTenantSession>();
            newSession.TenantId.ShouldBe(setup.ExpectedNewSessionTenantId);
        }
    }

    public static TheoryData<Configuration> Configurations =>
        new()
        {
            TheSame(Default, Default, TenancyStyle.Single),
            TheSame(Default, Default, TenancyStyle.Conjoined),
            TheSame(Default, NonDefault, TenancyStyle.Single),
            New(Default, NonDefault, TenancyStyle.Conjoined, NonDefault),
            TheSame(Default, NonDefault, TenancyStyle.Conjoined,
                isTenantStoredInCurrentDatabase: false
            ),
            New(NonDefault, Default, TenancyStyle.Single, Default),
            New(NonDefault, Default, TenancyStyle.Single, Default,
                allowAnyTenant: false,
                defaultTenantUsageEnabled: true
            ),
            New(NonDefault, Default, TenancyStyle.Single, Default,
                allowAnyTenant: true,
                defaultTenantUsageEnabled: false
            ),
            TheSame(NonDefault, Default, TenancyStyle.Single,
                allowAnyTenant: false,
                defaultTenantUsageEnabled: false
            ),
            TheSame(NonDefault, Default, TenancyStyle.Conjoined),
            New(NonDefault, NonDefault, TenancyStyle.Single, Default),
            New(NonDefault, NonDefault, TenancyStyle.Single, Default,
                allowAnyTenant: false,
                defaultTenantUsageEnabled: true
            ),
            New(NonDefault, NonDefault, TenancyStyle.Single, Default,
                allowAnyTenant: true,
                defaultTenantUsageEnabled: false
            ),
            TheSame(NonDefault, NonDefault, TenancyStyle.Single,
                allowAnyTenant: false,
                defaultTenantUsageEnabled: false
            ),
            TheSame(NonDefault, NonDefault, TenancyStyle.Conjoined),
        };

    private static readonly string Default = StorageConstants.DefaultTenantId;
    private static readonly string NonDefault = "NON_DEFAULT";

    public record Configuration(
        string SessionTenant,
        string SliceTenant,
        TenancyStyle StorageTenancyStyle,
        bool IsTenantStoredInCurrentDatabase,
        bool AllowAnyTenant,
        bool DefaultTenantUsageEnabled,
        bool ExpectsNewSession,
        string? ExpectedNewSessionTenantId = default
    )
    {
        public static Configuration TheSame(
            string sessionTenant,
            string sliceTenant,
            TenancyStyle storageTenancyStyle,
            bool isTenantStoredInCurrentDatabase = true,
            bool allowAnyTenant = true,
            bool defaultTenantUsageEnabled = true
        ) => new(
            sessionTenant,
            sliceTenant,
            storageTenancyStyle,
            isTenantStoredInCurrentDatabase,
            allowAnyTenant,
            defaultTenantUsageEnabled,
            false);

        public static Configuration New(
            string sessionTenant,
            string sliceTenant,
            TenancyStyle storageTenancyStyle,
            string? expectedNewSessionTenantId,
            bool allowAnyTenant = true,
            bool defaultTenantUsageEnabled = true
        ) => new(
            sessionTenant,
            sliceTenant,
            storageTenancyStyle,
            true,
            true,
            true,
            true,
            expectedNewSessionTenantId
        );
    }

    private static DocumentSessionBase SessionWith(Configuration setup) =>
        DocumentSessionStub.Setup(setup);

    private static IEventSlice SliceWith(Configuration setup)
    {
        var slice = Substitute.For<IEventSlice>();

        // ReSharper disable once ConstantConditionalAccessQualifier
        slice.Tenant.Returns(new Tenant(setup.SliceTenant, Substitute.For<IMartenDatabase>()));

        return slice;
    }

    private static IDocumentStorage StorageWith(Configuration setup)
    {
        var storage = Substitute.For<IDocumentStorage>();
        storage.TenancyStyle.Returns(setup.StorageTenancyStyle);
        return storage;
    }

    internal class DocumentSessionStub: DocumentSessionBase
    {
        public static DocumentSessionStub Setup(Configuration setup)
        {
            var tenancy = Substitute.For<ITenancy>();
            tenancy.IsTenantStoredInCurrentDatabase(default, default)
                .ReturnsForAnyArgs(setup.IsTenantStoredInCurrentDatabase);

            var options = new StoreOptions();
            options.Connection("dummy");
            options.Tenancy = tenancy;
            options.Advanced.DefaultTenantUsageEnabled = setup.DefaultTenantUsageEnabled;

            var sessionOptions =
                new SessionOptions
                {
                    Tenant = new Tenant(setup.SessionTenant, Substitute.For<IMartenDatabase>()),
                    AllowAnyTenant = setup.AllowAnyTenant
                };

            return new DocumentSessionStub(options, sessionOptions, setup.SessionTenant);
        }

        private DocumentSessionStub(StoreOptions options, SessionOptions sessionOptions, string tenantId):
            base(
                new DocumentStore(options),
                sessionOptions,
                Substitute.For<IConnectionLifetime>()
            ) =>
            TenantId = tenantId;

        protected internal override void ejectById<T>(long id) =>
            throw new NotImplementedException();

        protected internal override void ejectById<T>(int id) =>
            throw new NotImplementedException();

        protected internal override void ejectById<T>(Guid id) =>
            throw new NotImplementedException();

        protected internal override void ejectById<T>(string id) =>
            throw new NotImplementedException();
    }
}
