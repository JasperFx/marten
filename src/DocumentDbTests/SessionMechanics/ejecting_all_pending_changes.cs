using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics
{
    public class ejecting_all_pending_changes : IntegrationContext
    {
        public ejecting_all_pending_changes(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        #region sample_ejecting_all_document_changes
        [Fact]
        public void will_clear_all_document_changes()
        {
            theSession.Store(Target.Random());
            theSession.Insert(Target.Random());
            theSession.Update(Target.Random());

            theSession.PendingChanges.Operations().Any().ShouldBeTrue();

            theSession.EjectAllPendingChanges();

            theSession.PendingChanges.Operations().Any().ShouldBeFalse();
        }
        #endregion

        public class AEvent{}
        public class BEvent{}
        public class CEvent{}

        [Fact]
        public void  will_clear_all_event_operations()
        {
            theSession.Events.StartStream(Guid.NewGuid(), new AEvent(), new BEvent());
            theSession.Events.Append(Guid.NewGuid(), new CEvent());

            theSession.PendingChanges.Streams().Any().ShouldBeTrue();

            theSession.EjectAllPendingChanges();

            theSession.PendingChanges.Streams().Any().ShouldBeFalse();
        }
    }
}
