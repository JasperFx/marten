using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class ejecting_all_pending_changes : IntegrationContext
{
    public ejecting_all_pending_changes(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public void will_clear_all_document_changes()
    {
        #region sample_ejecting_all_document_changes
        theSession.Store(Target.Random());
        theSession.Insert(Target.Random());
        theSession.Update(Target.Random());

        theSession.PendingChanges.Operations().Any().ShouldBeTrue();

        theSession.EjectAllPendingChanges();

        theSession.PendingChanges.Operations().Any().ShouldBeFalse();
        #endregion
    }

    public class DBAEvent{}
    public class DBBEvent{}
    public class DBCEvent{}

    [Fact]
    public void  will_clear_all_event_operations()
    {
        theSession.Events.StartStream(Guid.NewGuid(), new DBAEvent(), new DBBEvent());
        theSession.Events.Append(Guid.NewGuid(), new DBCEvent());

        theSession.PendingChanges.Streams().Any().ShouldBeTrue();

        theSession.EjectAllPendingChanges();

        theSession.PendingChanges.Streams().Any().ShouldBeFalse();
    }
}
