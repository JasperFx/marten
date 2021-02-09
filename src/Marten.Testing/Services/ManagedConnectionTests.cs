using System;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class ManagedConnectionTests
    {
        [Fact]
        public async Task increments_the_request_count()
        {
            using (var connection = new ManagedConnection(new ConnectionSource(), new NulloRetryPolicy()))
            {
                connection.RequestCount.ShouldBe(0);

                connection.Execute(new NpgsqlCommand("select 1"));
                connection.RequestCount.ShouldBe(1);

                connection.Execute(new NpgsqlCommand("select 2"));
                connection.RequestCount.ShouldBe(2);

                connection.Execute(new NpgsqlCommand("select 3"));
                connection.RequestCount.ShouldBe(3);

                connection.Execute(new NpgsqlCommand("select 4"));
                connection.RequestCount.ShouldBe(4);

                await connection.ExecuteAsync(new NpgsqlCommand("select 5"));
                connection.RequestCount.ShouldBe(5);

            }
        }

        [Fact]
        public async Task log_execute_failure_1_async()
        {
            var logger = new RecordingLogger();
            using (var connection =
                new ManagedConnection(new ConnectionSource(), new NulloRetryPolicy()) {Logger = logger})
            {
                var ex = await Exception<Marten.Exceptions.MartenCommandException>.ShouldBeThrownByAsync(async () =>
                {
                    await connection.ExecuteAsync(new NpgsqlCommand("select foo from nonexistent"));
                });

                logger.OnBeforeExecuted.ShouldBe(1);

                logger.LastCommand.CommandText.ShouldBe("select foo from nonexistent");
                logger.LastException.ShouldBe(ex.InnerException);
            }
        }


        [Fact]
        public void log_execute_failure_2()
        {
            var logger = new RecordingLogger();
            using (var connection =
                new ManagedConnection(new ConnectionSource(), new NulloRetryPolicy()) {Logger = logger})
            {
                var cmd = new NpgsqlCommand("select foo from nonexistent");

                var ex = Exception<Marten.Exceptions.MartenCommandException>.ShouldBeThrownBy(() =>
                    connection.Execute(cmd));

                logger.LastCommand.ShouldBe(cmd);
                logger.LastException.ShouldBe(ex.InnerException);
            }
        }


        [Fact]
        public void log_execute_success_1()
        {
            var logger = new RecordingLogger();
            using (var connection =
                new ManagedConnection(new ConnectionSource(), new NulloRetryPolicy()) {Logger = logger})
            {
                connection.Execute(new NpgsqlCommand("select 1"));

                logger.LastCommand.CommandText.ShouldBe("select 1");
            }
        }


        [Fact]
        public async Task log_execute_success_1_async()
        {
            var logger = new RecordingLogger();
            using (var connection =
                new ManagedConnection(new ConnectionSource(), new NulloRetryPolicy()) {Logger = logger})
            {
                await connection.ExecuteAsync(new NpgsqlCommand("select 1"));

                logger.LastCommand.CommandText.ShouldBe("select 1");
            }
        }


        [Fact]
        public void log_execute_success_2()
        {
            var logger = new RecordingLogger();
            using (var connection =
                new ManagedConnection(new ConnectionSource(), new NulloRetryPolicy()) {Logger = logger})
            {
                var cmd = new NpgsqlCommand("select 1");
                connection.Execute(cmd);

                logger.LastCommand.ShouldBeSameAs(cmd);
            }
        }


        [Fact]
        public async Task log_execute_success_2_async()
        {
            var logger = new RecordingLogger();
            using (var connection =
                new ManagedConnection(new ConnectionSource(), new NulloRetryPolicy()) {Logger = logger})
            {
                var cmd = new NpgsqlCommand("select 1");
                await connection.ExecuteAsync(cmd);

                logger.LastCommand.ShouldBeSameAs(cmd);
            }
        }

    }

    public class RecordingLogger : IMartenSessionLogger
    {
        public NpgsqlCommand LastCommand;
        public Exception LastException;

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            throw new NotImplementedException();
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {
            OnBeforeExecuted++;
        }

        public int OnBeforeExecuted { get; set; }

        public void LogSuccess(NpgsqlCommand command)
        {
            LastCommand = command;
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            LastCommand = command;
            LastException = ex;
        }
    }
}
