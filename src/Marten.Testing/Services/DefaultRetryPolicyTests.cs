using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class DefaultRetryPolicyTests
    {
        [Fact]
        public async Task transient_exception_is_retried()
        {
            var runNumber = 0;
            using var connection = setUpManagedConnectionWithRetry(retryTimes: 1);
            var cmd = new NpgsqlCommand();

            await connection.ExecuteAsync(cmd, (c, tkn) =>
                {
                    if (runNumber++ == 0)
                    {
                        throw createNpgsqlException(true);
                    }

                    return Task.FromResult("");
                }
            );

            runNumber.ShouldBe(2);
            cmd.Dispose();
            connection.Dispose();
        }

        [Fact]
        public void transient_exception_is_retried_but_throws_eventually()
        {
            var runNumber = 0;
            var retryNumbers = new List<int>();
            var retryPolicy = DefaultRetryPolicy.Twice(sleep: x =>
            {
                retryNumbers.Add(x);
                return TimeSpan.FromMilliseconds(20);
            });

            using var connection = new ManagedConnection(new ConnectionSource(), retryPolicy);
            var cmd = new NpgsqlCommand();

            Exception<Marten.Exceptions.MartenCommandException>.ShouldBeThrownBy(() =>
            {
                connection.Execute(cmd, c =>
                    {
                        runNumber++;
                        throw createNpgsqlException(true);
                    }
                );
            });

            runNumber.ShouldBe(3);
            retryNumbers.ShouldBe(new [] { 1, 2 });

            cmd.Dispose();
            connection.Dispose();
        }

        [Fact]
        public async Task non_transient_exception_is_not_retried()
        {
            var runNumber = 0;
            using var connection = setUpManagedConnectionWithRetry(retryTimes: 1);
            var cmd = new NpgsqlCommand();

            Exception<Marten.Exceptions.MartenCommandException>.ShouldBeThrownBy(() =>
            {
                connection.Execute(cmd, c =>
                    {
                        runNumber++;
                        throw createNpgsqlException(false);
                    }
                );
            });

            runNumber.ShouldBe(1);
            cmd.Dispose();
            connection.Dispose();
        }

        private static NpgsqlException createNpgsqlException(bool transient)
        {
            var innerException = transient ? (Exception)new TimeoutException() : new DivideByZeroException();
            var ex = new NpgsqlException("exception occurred", innerException);
            return ex;
        }

        private static ManagedConnection setUpManagedConnectionWithRetry(int retryTimes)
        {
            // minimal sleep not to cause slowness
            var retryPolicy = DefaultRetryPolicy.Times(retryTimes, sleep: x => TimeSpan.FromMilliseconds(20));
            using var connection = new ManagedConnection(new ConnectionSource(), retryPolicy);
            return connection;
        }
    }
}
