using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class EventStreamStorageTests
    {
        [Fact]
        public void use_the_plain_append_event_function_when_async_and_js_are_disabled()
        {
            var graph = new EventGraph(new StoreOptions())
            {
                AsyncProjectionsEnabled = false,
                JavascriptProjectionsEnabled = false
            };

            var storage = new EventStreamStorage(graph);

            storage.AppendEventFunction.Name.ShouldBe("mt_append_event");
        }

        [Fact]
        public void use_rolling_buffer_version_of_append_event_function_when_async_is_enabled()
        {
            var graph = new EventGraph(new StoreOptions())
            {
                AsyncProjectionsEnabled = true,
                JavascriptProjectionsEnabled = false
            };

            var storage = new EventStreamStorage(graph);


            storage.AppendEventFunction.Name.ShouldBe("mt_append_event_with_buffering");
        }
    }
}