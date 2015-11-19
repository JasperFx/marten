using System;
using FubuCore;
using Marten.Events;
using Marten.Schema;
using Shouldly;

namespace Marten.Testing.Events
{
    public class plv8_transformation_functions_Tests : DocumentSessionFixture
    {
        private readonly IEventStore theEvents;

        public plv8_transformation_functions_Tests()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");

            theContainer.GetInstance<IDocumentSchema>().Events.StreamMappingFor<Quest>()
                .AddEvent(typeof (MembersJoined));


            theEvents = theContainer.GetInstance<Marten.Events.IEventStore>();
            theEvents.Administration.RebuildEventStoreSchema();
            theEvents.Administration.LoadProjections(directory);
            theEvents.Administration.InitializeEventStoreInDatabase();

        }

        public void apply_a_simple_transformation()
        {
            var joined = new MembersJoined
            {
                Day = 3,
                Location = "Baerlon",
                Members = new[] {"Min"},
                Id = Guid.NewGuid()
            };

            var stream = Guid.NewGuid();
            var json = theEvents.Transforms.Transform("location", stream, joined);
            var expectation = "{'Day':3,'Location':'Baerlon','Id':'EVENT','Quest':'STREAM'}"
                .Replace("EVENT", joined.Id.ToString())
                .Replace("STREAM", stream.ToString())
                .Replace('\'', '"');

            json.ShouldBe(expectation);
        }
    }
}