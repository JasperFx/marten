using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Session.RequestCounter
{
    public class request_counter_Tests
    {
        [Fact]
        public void no_requests_should_keep_counter_on_zero()
        {
            var requestCounter = CreateCounter();
            requestCounter.NumberOfRequests.ShouldBe(0);
        }

        [Fact]
        public void number_of_requests_should_increment_on_execute()
        {
            var requestCounter = CreateCounter();

            requestCounter.Execute((a) => {});

            requestCounter.NumberOfRequests.ShouldBe(1);
        }

        [Fact]
        public void number_of_requests_should_increment_on_execute_in_transaction()
        {
            var requestCounter = CreateCounter();

            requestCounter.ExecuteInTransaction((a) => { });

            requestCounter.NumberOfRequests.ShouldBe(1);
        }

        [Fact]
        public void number_of_requests_should_increment_on_execute_sql()
        {
            var requestCounter = CreateCounter();

            requestCounter.Execute("sql");

            requestCounter.NumberOfRequests.ShouldBe(1);
        }

        [Fact]
        public void number_of_requests_should_increment_on_execute_T()
        {
            var requestCounter = CreateCounter();
            
            requestCounter.Execute(connection => new User());

            requestCounter.NumberOfRequests.ShouldBe(1);
        }

        [Fact]
        public void number_of_requests_should_increment_on_query_json()
        {
            var requestCounter = CreateCounter();

            requestCounter.QueryJson(new NpgsqlCommand());

            requestCounter.NumberOfRequests.ShouldBe(1);
        }

        [Fact]
        public void number_of_requests_should_increment_on_query_scalar()
        {
            var requestCounter = CreateCounter();

            requestCounter.QueryScalar<User>("sql");

            requestCounter.NumberOfRequests.ShouldBe(1);
        }

        [Fact]
        public void subsequent_number_of_requests_should_increment_on_execute()
        {
            var requestCounter = CreateCounter();

            requestCounter.Execute((a) => { });
            requestCounter.Execute((a) => { });
            requestCounter.Execute((a) => { });

            requestCounter.NumberOfRequests.ShouldBe(3);
        }

        private Marten.Services.RequestCounter CreateCounter()
        {
            return new Marten.Services.RequestCounter(new FakeCommandRunner());
        }
    }

    public class FakeCommandRunner : ICommandRunner
    {
        public void Execute(Action<NpgsqlConnection> action)
        {
            
        }

        public void ExecuteInTransaction(Action<NpgsqlConnection> action)
        {
            
        }

        public T Execute<T>(Func<NpgsqlConnection, T> func)
        {
            return default(T);
        }

        public IEnumerable<string> QueryJson(NpgsqlCommand cmd)
        {
            return Enumerable.Empty<string>();
        }

        public int Execute(string sql)
        {
            return 1;
        }

        public T QueryScalar<T>(string sql)
        {
            return default(T);
        }
    }
}