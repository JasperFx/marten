using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Events;
using Marten.Internal;
using Marten.Storage;
using Marten.Testing.Events.V4Concepts;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class StreamActionTester
    {
        private IMartenSession theSession;
        private ITenant theTenant;

        public StreamActionTester()
        {
            theSession = Substitute.For<IMartenSession>();
            theTenant = Substitute.For<ITenant>();
            theSession.Tenant.Returns(theTenant);
            theTenant.TenantId.Returns("TX");
        }

        [Fact]
        public void ApplyServerVersion_for_new_streams()
        {
            var action = StreamAction.Start(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());

            var queue = new Queue<long>();
            queue.Enqueue(11);
            queue.Enqueue(12);
            queue.Enqueue(13);
            queue.Enqueue(14);
            action.PrepareEvents(0, new EventGraph(new StoreOptions()), queue, theSession);


            action.Events[0].Version.ShouldBe(1);
            action.Events[1].Version.ShouldBe(2);
            action.Events[2].Version.ShouldBe(3);
            action.Events[3].Version.ShouldBe(4);

            action.Events[0].Sequence.ShouldBe(11);
            action.Events[1].Sequence.ShouldBe(12);
            action.Events[2].Sequence.ShouldBe(13);
            action.Events[3].Sequence.ShouldBe(14);
        }

        [Fact]
        public void ApplyServerVersion_for_existing_streams()
        {
            var action = StreamAction.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());

            var queue = new Queue<long>();
            queue.Enqueue(11);
            queue.Enqueue(12);
            queue.Enqueue(13);
            queue.Enqueue(14);


            action.PrepareEvents(5, new EventGraph(new StoreOptions()),queue, theSession);

            action.ExpectedVersionOnServer.ShouldBe(5);


            action.Events[0].Version.ShouldBe(6);
            action.Events[1].Version.ShouldBe(7);
            action.Events[2].Version.ShouldBe(8);
            action.Events[3].Version.ShouldBe(9);
        }
    }
}
