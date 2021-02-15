using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Npgsql;
using Xunit;

namespace Marten.Testing.Examples
{
    #region sample_retrypolicy-samplepolicy
    // Implement IRetryPolicy interface
    public sealed class ExceptionFilteringRetryPolicy: IRetryPolicy
    {
        private readonly int maxTries;
        private readonly Func<Exception, bool> filter;

        private ExceptionFilteringRetryPolicy(int maxTries, Func<Exception, bool> filter)
        {
            this.maxTries = maxTries;
            this.filter = filter;
        }

        public static IRetryPolicy Once(Func<Exception, bool> filter = null)
        {
            return new ExceptionFilteringRetryPolicy(2, filter ?? (_ => true));
        }

        public static IRetryPolicy Twice(Func<Exception, bool> filter = null)
        {
            return new ExceptionFilteringRetryPolicy(3, filter ?? (_ => true));
        }

        public static IRetryPolicy NTimes(int times, Func<Exception, bool> filter = null)
        {
            return new ExceptionFilteringRetryPolicy(times + 1, filter ?? (_ => true));
        }

        public void Execute(Action operation)
        {
            Try(() => { operation(); return Task.CompletedTask; }, CancellationToken.None).GetAwaiter().GetResult();
        }

        public TResult Execute<TResult>(Func<TResult> operation)
        {
            return Try(() => Task.FromResult(operation()), CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            return Try(operation, cancellationToken);
        }

        public Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken)
        {
            return Try(operation, cancellationToken);
        }

        private async Task Try(Func<Task> operation, CancellationToken token)
        {
            for (var tries = 0; ; token.ThrowIfCancellationRequested())
            {
                try
                {
                    await operation().ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (++tries < maxTries && filter(e))
                {
                }
            }
        }

        private async Task<T> Try<T>(Func<Task<T>> operation, CancellationToken token)
        {
            for (var tries = 0; ; token.ThrowIfCancellationRequested())
            {
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception e) when (++tries < maxTries && filter(e))
                {
                }
            }
        }
    }

    #endregion sample_retrypolicy-samplepolicy

    public sealed class RetryPolicyTests: IntegrationContext
    {
        [Fact]
        public void CanPlugInRetryPolicyThatRetriesOnException()
        {
            var m = new List<string>();
            StoreOptions(c =>
            {
                #region sample_retrypolicy-samplepolicy-pluggingin
                // Plug in our custom retry policy via StoreOptions
                // We retry operations twice if they yield and NpgsqlException that is transient
                c.RetryPolicy(ExceptionFilteringRetryPolicy.Twice(e => e is NpgsqlException ne && ne.IsTransient));
                #endregion sample_retrypolicy-samplepolicy-pluggingin

                #region sample_retrypolicy-samplepolicy-default
                // Use DefaultRetryPolicy which handles Postgres's transient errors by default with sane defaults
                // We retry operations twice if they yield and NpgsqlException that is transient
                // Each error will cause sleep of N seconds where N is the current retry number
                c.RetryPolicy(DefaultRetryPolicy.Twice());
                #endregion sample_retrypolicy-samplepolicy-default

                // For unit test, use one that checks that exception is not transient
                c.RetryPolicy(ExceptionFilteringRetryPolicy.Twice(e => e is NpgsqlException ne && !ne.IsTransient));
                // For unit test, override the policy with one that captures messages
                c.RetryPolicy(ExceptionFilteringRetryPolicy.Twice(e =>
                {
                    if (e is NpgsqlException ne && !ne.IsTransient)
                    {
                        m.Add(e.Message);
                        return true;
                    }

                    return false;
                }));
            });

            using (var s = theStore.QuerySession())
            {
                Assert.Throws<Marten.Exceptions.MartenCommandException>(() =>
                {
                    var _ = s.Query<object>("select null from mt_nonexistenttable").FirstOrDefault();
                });
            }

            // Our retry exception filter should have triggered twice
            Assert.True(m.Count(s => s.IndexOf("relation \"mt_nonexistenttable\" does not exist", StringComparison.OrdinalIgnoreCase) > -1) == 2);
        }

        public RetryPolicyTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
