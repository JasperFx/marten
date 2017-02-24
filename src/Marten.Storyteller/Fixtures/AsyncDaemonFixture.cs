using System;
using StoryTeller;
using StoryTeller.Grammars.Tables;

namespace Marten.Storyteller.Fixtures
{
    public class AsyncDaemonFixture : Fixture
    {
        public void EventSchemaIs(string schema)
        {
            throw new NotImplementedException();
        }

        public void LeadingEdgeBuffer(string seconds)
        {
            throw new NotImplementedException();
        }

        public void PublishAllEvents()
        {
            throw new NotImplementedException();
        }

        public void PublishAllEventsAsync()
        {
            throw new NotImplementedException();
        }

        public void StartTheDaemon()
        {
            throw new NotImplementedException();
        }

        public void StopWhenFinished()
        {
            throw new NotImplementedException();
        }

        public void UseTheErroringProjection()
        {
            throw new NotImplementedException();
        }

        [ExposeAsTable("Compare Projects to the Expected Aggregate")]
        public void CompareProjects(string Project)
        {
            throw new NotImplementedException();
        }

        public void RebuildProjection()
        {
            throw new NotImplementedException();
        }

        public void CreateSequentialGap(string original, string seq)
        {
            throw new NotImplementedException();
        }
    }
}