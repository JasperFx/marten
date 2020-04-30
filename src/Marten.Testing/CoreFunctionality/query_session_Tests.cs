using System;
using System.Data;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
	public class query_session_Tests : IntegrationContext
	{
		[Fact]
		public void should_respect_command_timeout_options()
		{
			using (var session = theStore.QuerySession(new SessionOptions() { Timeout = -1 }))
			{
				var e = Assert.Throws<ArgumentOutOfRangeException>(() => session.Query<int>("select 1"));
				Assert.StartsWith("CommandTimeout can't be less than zero", e.Message);
			}
		}

		[Fact]
		public void should_respect_isolationlevel_and_be_read_only_transaction_when_serializable_isolation()
		{
			var user = new User();

			theStore.BulkInsertDocuments(new [] { user });
			using (var session = theStore.QuerySession(new SessionOptions() { IsolationLevel = IsolationLevel.Serializable, Timeout = 1 }))
			{
				using (var cmd = session.Connection.CreateCommand("delete from mt_doc_user"))
				{
					var e = Assert.Throws<PostgresException>(() => cmd.ExecuteNonQuery());

					// ERROR: cannot execute DELETE in a read-only transaction
					// read_only_sql_transaction
					Assert.Equal("25006", e.SqlState);
				}
			}
		}

        public query_session_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
