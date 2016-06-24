using System;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class document_session_logs_SaveChanges_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void records_on_SaveChanges()
        {
            var logger = new RecordingLogger();

            theSession.Logger = logger;

            theSession.SaveChanges();

            logger.LastSession.ShouldBe(theSession);
        }

        [Fact]
        public async Task records_on_SaveChangesAsync()
        {
            var logger = new RecordingLogger();

            theSession.Logger = logger;

            await theSession.SaveChangesAsync().ConfigureAwait(false);

            logger.LastSession.ShouldBe(theSession);
        }
    }

    public class RecordingLogger : IMartenSessionLogger
    {
        public void LogSuccess(NpgsqlCommand command)
        {
            
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
        }

        public void RecordSavedChanges(IDocumentSession session)
        {
            LastSession = session;
        }

        public IDocumentSession LastSession { get; set; }
    }
}