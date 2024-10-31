#nullable enable
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests.Internal.Sessions
{
    // EventTracingConnectionLifetime cannot be tested in parallel with other tests
    [CollectionDefinition(nameof(EventTracingConnectionLifetimeCollection), DisableParallelization = true)]
    public class EventTracingConnectionLifetimeCollection
    {
    }

    [Collection(nameof(EventTracingConnectionLifetimeCollection))]
    public class EventTracingConnectionLifetimeTests : IAsyncLifetime
    {
        private NpgsqlCommand _npgsqlCommand = new("select 1");
        private IConnectionLifetime? _innerConnectionLifetime = Substitute.For<IConnectionLifetime>();
        private bool _startCalled;
        private bool _endCalled;
        private List<OperationPage> _batchPages =new();
        private List<Exception> _exceptions =new();
        private BatchBuilder _batchBuilder =new();
        private NpgsqlBatch _batch;
        private DataTable _dataTable =new();
        private DbDataReader _dataReader;

        private const string MartenCommandExecutionStarted = "marten.command.execution.started";
        private const string MartenBatchExecutionStarted = "marten.batch.execution.started";
        private const string MartenBatchPagesExecutionStarted = "marten.batch.pages.execution.started";

        private const string DefaultTenant = "default";

        //Taken from the OpenTelemetry package as they are internal.
        private const string AttributeExceptionEventName = "exception";
        private const string AttributeExceptionType = "exception.type";
        private const string AttributeExceptionMessage = "exception.message";
        private const string AttributeExceptionStacktrace = "exception.stacktrace";

        public Task InitializeAsync()
        {
            _batch = _batchBuilder.Compile();
            _dataReader = _dataTable.CreateDataReader();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _batch.DisposeAsync();
            await _dataReader.DisposeAsync();
        }

        [Fact]
        public void Ctor_Should_Throw_Argument_Null_Exception_When_Inner_Connection_Lifetime_Is_Null()
        {
            _innerConnectionLifetime = null;
            var act = () => new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new());
            Should.Throw<ArgumentNullException>(act);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void Ctor_Should_Throw_Argument_Exception_When_Tenant_Id_Is_Null(string tenantId)
        {
            var act = () => new EventTracingConnectionLifetime(_innerConnectionLifetime, tenantId, new());
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBe(default);
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.ShouldNotBe(default);
                    expectedEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            _innerConnectionLifetime.Execute(_npgsqlCommand).Returns(1);
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBe(default);
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
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
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBe(default);
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.ShouldNotBe(default);
                    expectedEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            _innerConnectionLifetime.ExecuteAsync(_npgsqlCommand, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(1));
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBe(default);
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
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
            using (var eventTracingConnectionLifetime =new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                await Should.ThrowAsync<InvalidOperationException>(() => eventTracingConnectionLifetime.ExecuteAsync(_npgsqlCommand));
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteAsync(_npgsqlCommand, Arg.Any<CancellationToken>());
        }

        [Fact]
        public void ExecuteReader_Ensure_The_Correct_Event_And_Tags_Are_Emited_When_Command_Execution_Succeeds()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.ShouldNotBe(default);
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.ShouldNotBe(default);
                    expectedEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            using var dataTable = new DataTable();
            using var dataReader = dataTable.CreateDataReader();
            _innerConnectionLifetime.ExecuteReader(_npgsqlCommand).Returns(dataReader);
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                eventTracingConnectionLifetime.ExecuteReader(_npgsqlCommand);
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteReader(_npgsqlCommand);
        }

        [Fact]
        public void ExecuteReader_Ensure_The_Correct_Events_And_Tags_Are_Emited_When_Command_Execution_Fails()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
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
                _innerConnectionLifetime.ExecuteReader(_npgsqlCommand).Throws<InvalidOperationException>();
                using (var eventTracingConnectionLifetime =
                       new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
                {
                    Should.Throw<InvalidOperationException>(
                        () => eventTracingConnectionLifetime.ExecuteReader(_npgsqlCommand));
                }

                _startCalled.ShouldBeTrue();
                _endCalled.ShouldBeTrue();
                _innerConnectionLifetime.Received(1).ExecuteReader(_npgsqlCommand);
            }

        [Fact]
        public async Task ExecuteReaderAsync_Ensure_The_Correct_Event_And_Tags_Are_Emited_When_Command_Execution_Succeeds()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.Name.ShouldBe(MartenCommandExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            _dataReader =_dataTable.CreateDataReader();
            _innerConnectionLifetime.ExecuteReaderAsync(_npgsqlCommand, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(_dataReader));
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                await eventTracingConnectionLifetime.ExecuteReaderAsync(_npgsqlCommand);
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteReaderAsync(_npgsqlCommand, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteReaderAsync_Ensure_The_Correct_Events_And_Tags_Are_Emited_When_Command_Execution_Fails()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
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
            using (var eventTracingConnectionLifetime = new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                await Should.ThrowAsync<InvalidOperationException>(() => eventTracingConnectionLifetime.ExecuteAsync(_npgsqlCommand));
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteAsync(_npgsqlCommand, Arg.Any<CancellationToken>());
        }

        [Fact]
        public void ExecuteReaderWithBatch_Ensure_The_Correct_Event_And_Tags_Are_Emited_When_Command_Execution_Succeeds()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.Name.ShouldBe(MartenBatchExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            using var dataTable = new DataTable();
            using DbDataReader dataReader = dataTable.CreateDataReader();
            _innerConnectionLifetime.ExecuteReader(Arg.Any<NpgsqlBatch>()).Returns(dataReader);
                using (var eventTracingConnectionLifetime =
                       new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
                {
                    eventTracingConnectionLifetime.ExecuteReader(_batch);
                }

                _startCalled.ShouldBeTrue();
                _endCalled.ShouldBeTrue();
                _innerConnectionLifetime.Received(1).ExecuteReader(Arg.Any<NpgsqlBatch>());
            }

        [Fact]
        public void ExecuteReaderWithBatch_Ensure_The_Correct_Events_And_Tags_Are_Emited_When_Command_Execution_Fails()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    activity.Events.Count().ShouldBe(2);
                    var firstEvent = activity.Events.First();
                    firstEvent.Name.ShouldBe(MartenBatchExecutionStarted);
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
                _innerConnectionLifetime.ExecuteReader(Arg.Any<NpgsqlBatch>()).Throws<InvalidOperationException>();
                using (var eventTracingConnectionLifetime =
                       new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                Should.Throw<InvalidOperationException>(() => eventTracingConnectionLifetime.ExecuteReader(_batch));
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteReader(Arg.Any<NpgsqlBatch>());
        }

        [Fact]
        public async Task ExecuteReaderWithBatchAsync_Ensure_The_Correct_Event_And_Tags_Are_Emited_When_Command_Execution_Succeeds()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.Name.ShouldBe(MartenBatchExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            using var dataTable = new DataTable();
            using DbDataReader dataReader = dataTable.CreateDataReader();
            _innerConnectionLifetime.ExecuteReaderAsync(Arg.Any<NpgsqlBatch>()).Returns(Task.FromResult(dataReader));
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                await eventTracingConnectionLifetime.ExecuteReaderAsync(_batch);
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteReaderAsync(Arg.Any<NpgsqlBatch>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteReaderWithBatchAsync_Ensure_The_Correct_Events_And_Tags_Are_Emited_When_Command_Execution_Fails()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    activity.Events.Count().ShouldBe(2);
                    var firstEvent = activity.Events.First();
                    firstEvent.Name.ShouldBe(MartenBatchExecutionStarted);
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
            _innerConnectionLifetime.ExecuteReaderAsync(Arg.Any<NpgsqlBatch>(), Arg.Any<CancellationToken>())
                .ThrowsAsync<InvalidOperationException>();
            using (var eventTracingConnectionLifetime = new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                await Should.ThrowAsync<InvalidOperationException>(() => eventTracingConnectionLifetime.ExecuteReaderAsync(_batch));
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1).ExecuteReaderAsync(Arg.Any<NpgsqlBatch>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteBatchPagesAsync_Ensure_The_Correct_Event_And_Tags_Are_Emited_When_Command_Execution_Succeeds()
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
                    activity.DisplayName.ShouldBe("marten.connection");
                },
                ActivityStopped = activity =>
                {
                    _endCalled = true;
                    activity.ShouldNotBeNull();
                    var expectedTag = activity.Tags.SingleOrDefault();
                    expectedTag.Key.ShouldBe(MartenTracing.TenantId);
                    var expectedEvent = activity.Events.SingleOrDefault();
                    expectedEvent.Name.ShouldBe(MartenBatchPagesExecutionStarted);
                    expectedEvent.Tags.ShouldBeEmpty();
                }
            };

            ActivitySource.AddActivityListener(listener);
            _innerConnectionLifetime.ExecuteBatchPagesAsync(_batchPages, _exceptions, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(1));
            using (var eventTracingConnectionLifetime =
                   new EventTracingConnectionLifetime(_innerConnectionLifetime, DefaultTenant, new()))
            {
                await eventTracingConnectionLifetime.ExecuteBatchPagesAsync(_batchPages, _exceptions, CancellationToken.None);
            }

            _startCalled.ShouldBeTrue();
            _endCalled.ShouldBeTrue();
            _innerConnectionLifetime.Received(1)
                .ExecuteBatchPagesAsync(_batchPages, _exceptions, CancellationToken.None);
        }


    }
}
