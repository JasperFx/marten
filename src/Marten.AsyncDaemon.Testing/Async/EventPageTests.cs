using System.Collections.Generic;
using Marten.Events;
using Marten.Testing.CodeTracker;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{
    public class EventPageTests
    {
        [Fact]
        public void perfectly_sequential()
        {
            var list = new List<long>() { 3, 4, 5, 6, 7 };

            EventPage.IsCompletelySequential(list)
                .ShouldBeTrue();
        }

        [Fact]
        public void not_sequential()
        {
            var list = new List<long>() { 3, 4, 5, 6, 7, 10 };

            EventPage.IsCompletelySequential(list)
                .ShouldBeFalse();
        }

        [Fact]
        public void last_encountered_empty_page()
        {
            var page = new EventPage(0, 100, new List<IEvent>())
            {
                NextKnownSequence = 150,
                LastKnownSequence = 1000
            };

            page.LastEncountered().ShouldBe(149);
        }

        [Fact]
        public void last_encountered_with_no_known_sequence()
        {
            var page = new EventPage(0, 100, new List<IEvent>())
            {
                NextKnownSequence = 0,
                LastKnownSequence = 1000
            };

            page.LastEncountered().ShouldBe(1000);
        }

        [Fact]
        public void last_encountered_with_non_zero_page()
        {
            var page = new EventPage(0, 100, new List<IEvent> { new Event<ProjectStarted>(new ProjectStarted()) })
            {
                NextKnownSequence = 0,
                LastKnownSequence = 1000
            };

            page.Sequences.Add(97);
            page.Sequences.Add(98);
            page.Sequences.Add(99);

            page.LastEncountered().ShouldBe(1000);
        }

        [Fact]
        public void should_pause_if_empty_with_no_known_next()
        {
            var page = new EventPage(0, 100, new List<IEvent>())
            {
                NextKnownSequence = 0,
                LastKnownSequence = 1000
            };

            page.ShouldPause().ShouldBeTrue();
        }

        [Fact]
        public void should_not_pause_if_there_is_a_next_known_sequence()
        {
            var page = new EventPage(0, 100, new List<IEvent>())
            {
                NextKnownSequence = 300,
                LastKnownSequence = 1000
            };

            page.ShouldPause().ShouldBeFalse();
        }

        [Fact]
        public void should_pause_if_there_are_any_events()
        {
            var page = new EventPage(0, 100, new List<IEvent> { new Event<ProjectStarted>(new ProjectStarted()) })
            {
                NextKnownSequence = 0,
                LastKnownSequence = 1000
            };

            page.Sequences.Add(97);
            page.Sequences.Add(98);
            page.Sequences.Add(99);

            page.ShouldPause().ShouldBeTrue();
        }
    }
}
