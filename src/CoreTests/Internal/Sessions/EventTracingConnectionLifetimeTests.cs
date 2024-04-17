#nullable enable
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CoreTests.Internal.Sessions
{
    // EventTracingConnectionLifetime cannot be tested in parallel with other tests
    [CollectionDefinition(nameof(EventTracingConnectionLifetimeCollection), DisableParallelization = true)]
    public class EventTracingConnectionLifetimeCollection
    {
    }

    [Collection(nameof(EventTracingConnectionLifetimeCollection))]
    public class EventTracingConnectionLifetimeTests
    {
        private NpgsqlCommand _npgsqlCommand = new("select 1");
        private IConnectionLifetime? _innerConnectionLifetime = Substitute.For<IConnectionLifetime>();
        private bool _startCalled;
        private bool _endCalled;
        private const string MartenCommandExecutionStarted = "marten.command.execution.started";
        private const string MartenBatchExecutionStarted = "marten.batch.execution.started";
        private const string MartenBatchPagesExecutionStarted = "marten.batch.pages.execution.started";

        private const string DefaultTenant = "default";

        //Taken from the OpenTelemetry package as they are internal.
        private const string AttributeExceptionEventName = "exception";
        private const string AttributeExceptionType = "exception.type";
        private const string AttributeExceptionMessage = "exception.message";
        private const string AttributeExceptionStacktrace = "exception.stacktrace";

        [Fact]
        public void Ctor_Should_Throw_Argument_Null_Exception_When_Inner_Connection_Lifetime_Is_Null()
        {
            _innerConnectionLifetime = null;
            var act = () => new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant);
            Should.Throw<ArgumentNullException>(act);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void Ctor_Should_Throw_Argument_Exception_When_Tenant_Id_Is_Null(string tenantId)
        {
            var act = () => new EventTracingConnectionLifetime(_innerConnectionLifetime, tenantId);
            Should.Throw<ArgumentException>(act);
        }

        [Fact]
        public void Execute_Ensure_The_Correct_Event_And_Tags_Are_Emited_When_Command_Execution_Succeeds()
        {
            _startCalled = false;
            _endCalled = false;

            using var listener = new ActivityListener
            {
                ShouldListenTo = _ => _.Name == "Marten",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                    _startCalled = true;
                    activity.ShouldNotBeNull();
                    activity.DisplayName.ShouldBe("connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBeNull();
                    expectedTag.Key.ShouldBe(MartenTracing.MartenTenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.ShouldNotBeNull();
                    expectedEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            _innerConnectionLifetime.Execute(_npgsqlCommand).Returns(1);
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant))
            {
                eventTracingConnectionLifetime.Execute(_npgsqlCommand);
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).Execute(_npgsqlCommand);
        }

        [Fact]
        public void Execute_Ensure_The_Correct_Events_And_Tags_Are_Emited_When_Command_Execution_Fails()
        {
            _startCalled = false;
            _endCalled = false;

            using var listener = new ActivityListener
            {
                ShouldListenTo = _ => _.Name == "Marten",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                    _startCalled = true;
                    activity.ShouldNotBeNull();
                    activity.DisplayName.ShouldBe("connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBeNull();
                    expectedTag.Key.ShouldBe(MartenTracing.MartenTenantId);
                    activity.Events.Count().ShouldBe(2);
                    var firstEvent = activity.Events.First();
                    firstEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    firstEvent.Tags.ShouldBeEmpty();
                    var lastEvent = activity.Events.Last();
                    lastEvent.Name.ShouldBe(AttributeExceptionEventName);
                    lastEvent.Tags.Select(x => x.Key)
                        .ShouldBe(
                            new[] { AttributeExceptionType, AttributeExceptionStacktrace, AttributeExceptionMessage },
                            ignoreOrder: true);
                }
            };

            ActivitySource.AddActivityListener(listener);
            _innerConnectionLifetime.Execute(_npgsqlCommand).Throws<InvalidOperationException>();
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant))
            {
                Should.Throw<InvalidOperationException>(() => eventTracingConnectionLifetime.Execute(_npgsqlCommand));
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).Execute(_npgsqlCommand);
        }

        [Fact]
        public async Task ExecuteAsync_Ensure_The_Correct_Event_And_Tags_Are_Emited_When_Command_Execution_Succeeds()
        {
            _startCalled = false;
            _endCalled = false;

            using var listener = new ActivityListener
            {
                ShouldListenTo = _ => _.Name == "Marten",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                    _startCalled = true;
                    activity.ShouldNotBeNull();
                    activity.DisplayName.ShouldBe("connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBeNull();
                    expectedTag.Key.ShouldBe(MartenTracing.MartenTenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.ShouldNotBeNull();
                    expectedEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            _innerConnectionLifetime.ExecuteAsync(_npgsqlCommand, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(1));
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant))
            {
                await eventTracingConnectionLifetime.ExecuteAsync(_npgsqlCommand);
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteAsync(_npgsqlCommand, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_Ensure_The_Correct_Events_And_Tags_Are_Emited_When_Command_Execution_Fails()
        {
            _startCalled = false;
            _endCalled = false;

            using var listener = new ActivityListener
            {
                ShouldListenTo = _ => _.Name == "Marten",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                    _startCalled = true;
                    activity.ShouldNotBeNull();
                    activity.DisplayName.ShouldBe("connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBeNull();
                    expectedTag.Key.ShouldBe(MartenTracing.MartenTenantId);
                    activity.Events.Count().ShouldBe(2);
                    var firstEvent = activity.Events.First();
                    firstEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    firstEvent.Tags.ShouldBeEmpty();
                    var lastEvent = activity.Events.Last();
                    lastEvent.Name.ShouldBe(AttributeExceptionEventName);
                    lastEvent.Tags.Select(x => x.Key)
                        .ShouldBe(
                            new[] { AttributeExceptionType, AttributeExceptionStacktrace, AttributeExceptionMessage },
                            ignoreOrder: true);
                    }
            };

            ActivitySource.AddActivityListener(listener);
                _innerConnectionLifetime.ExecuteAsync(_npgsqlCommand, Arg.Any<CancellationToken>())
                    .ThrowsAsync<InvalidOperationException>();
            using (var eventTracingConnectionLifetime =new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant))
            {
                await Should.ThrowAsync<InvalidOperationException>(() => eventTracingConnectionLifetime.ExecuteAsync(_npgsqlCommand));
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteAsync(_npgsqlCommand, Arg.Any<CancellationToken>());
        }
    }
}
