using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class ManagedConnectionTests: IntegrationContext
    {
        private readonly RecordingLogger logger = new();

        public ManagedConnectionTests(DefaultStoreFixture fixture) : base(fixture)
        {
            theSession.Logger = logger;
        }

        [Fact]
        public async Task increments_the_request_count()
        {
            theSession.RequestCount.ShouldBe(0);

            theSession.Execute(new NpgsqlCommand("select 1"));
            theSession.RequestCount.ShouldBe(1);

            theSession.Execute(new NpgsqlCommand("select 2"));
            theSession.RequestCount.ShouldBe(2);

            theSession.Execute(new NpgsqlCommand("select 3"));
            theSession.RequestCount.ShouldBe(3);

            theSession.Execute(new NpgsqlCommand("select 4"));
            theSession.RequestCount.ShouldBe(4);

            await theSession.ExecuteAsync(new NpgsqlCommand("select 5"));
            theSession.RequestCount.ShouldBe(5);
        }

        [Fact]
        public async Task log_execute_failure_1_async()
        {
            var ex = await Exception<MartenCommandException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.ExecuteAsync(new NpgsqlCommand("select foo from nonexistent"));
            });

            logger.OnBeforeExecuted.ShouldBe(1);

            logger.LastCommand.CommandText.ShouldBe("select foo from nonexistent");
            logger.LastException.ShouldBe(ex.InnerException);
        }


        [Fact]
        public void log_execute_failure_2()
        {

            var cmd = new NpgsqlCommand("select foo from nonexistent");

            var ex = Exception<MartenCommandException>.ShouldBeThrownBy(() =>
                theSession.Execute(cmd));

            logger.LastCommand.ShouldBe(cmd);
            logger.LastException.ShouldBe(ex.InnerException);

        }


        [Fact]
        public void log_execute_success_1()
        {
            theSession.Execute(new NpgsqlCommand("select 1"));

            logger.LastCommand.CommandText.ShouldBe("select 1");

        }


        [Fact]
        public async Task log_execute_success_1_async()
        {
            await theSession.ExecuteAsync(new NpgsqlCommand("select 1"));

            logger.LastCommand.CommandText.ShouldBe("select 1");
        }


        [Fact]
        public void log_execute_success_2()
        {
            var cmd = new NpgsqlCommand("select 1");
            theSession.Execute(cmd);

            logger.LastCommand.ShouldBeSameAs(cmd);
        }


        [Fact]
        public async Task log_execute_success_2_async()
        {
            var cmd = new NpgsqlCommand("select 1");
            await theSession.ExecuteAsync(cmd);

            logger.LastCommand.ShouldBeSameAs(cmd);
        }
    }

    public class RecordingLogger: IMartenSessionLogger
    {
        public NpgsqlCommand LastCommand;
        public Exception LastException;

        public int OnBeforeExecuted { get; set; }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {
            OnBeforeExecuted++;
        }

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
