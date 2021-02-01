using System;
using System.Linq;
using Baseline.Dates;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Resiliency
{
    public class ExceptionPolicyTests
    {
        [Fact]
        public void filter_by_exception_general()
        {
            var policy = new ExceptionPolicy(new DaemonSettings(), e => e is NpgsqlException);

            policy.Matches(new NpgsqlException()).ShouldBeTrue();
            policy.Matches(new DivideByZeroException()).ShouldBeFalse();
        }

        [Fact]
        public void filter_by_exception_type()
        {
            var settings = new DaemonSettings();
            settings.OnException<NpgsqlException>();

            var policy = settings.Policies.Single();

            policy.Matches(new NpgsqlException()).ShouldBeTrue();
            policy.Matches(new DivideByZeroException()).ShouldBeFalse();
        }

        [Fact]
        public void filter_by_exception_type_2()
        {
            var settings = new DaemonSettings();
            settings.OnExceptionOfType(typeof(NpgsqlException));

            var policy = settings.Policies.Single();

            policy.Matches(new NpgsqlException()).ShouldBeTrue();
            policy.Matches(new DivideByZeroException()).ShouldBeFalse();
        }

        [Fact]
        public void filter_by_exception_type_and_more()
        {
            var settings = new DaemonSettings();
            settings.OnException<CustomException>(x => x.ErrorCode == 200);

            var policy = settings.Policies.Single();

            policy.Matches(new NpgsqlException()).ShouldBeFalse();
            policy.Matches(new CustomException(200)).ShouldBeTrue();
            policy.Matches(new CustomException(201)).ShouldBeFalse();
        }

        [Fact]
        public void and_on_inner_exception_by_type()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner<CustomException>();

            var policy = settings.Policies.Single();

            policy.Matches(new InvalidCastException()).ShouldBeFalse();
            policy.Matches(new InvalidOperationException()).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new DivideByZeroException())).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new CustomException(111))).ShouldBeTrue();

        }

        [Fact]
        public void and_on_inner_exception_by_type_and_condition()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner<CustomException>(x => x.ErrorCode == 500);

            var policy = settings.Policies.Single();

            policy.Matches(new InvalidCastException()).ShouldBeFalse();
            policy.Matches(new InvalidOperationException()).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new DivideByZeroException())).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new CustomException(111))).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new CustomException(500))).ShouldBeTrue();

        }

        [Fact]
        public void and_on_inner_exception_by_type_and_condition_2()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner(x => (x is CustomException {ErrorCode: 500}));

            var policy = settings.Policies.Single();

            policy.Matches(new InvalidCastException()).ShouldBeFalse();
            policy.Matches(new InvalidOperationException()).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new DivideByZeroException())).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new CustomException(111))).ShouldBeFalse();
            policy.Matches(new InvalidOperationException("boom.",new CustomException(500))).ShouldBeTrue();

        }

        [Fact]
        public void registering_retries()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner(x => (x is CustomException {ErrorCode: 500}));

            var policy = settings.Policies.Single();

            policy.RetryLater(1.Seconds(), 3.Seconds(), 5.Seconds());

            policy.Continuations.Count.ShouldBe(3);
            policy.Continuations[0].ShouldBe(new RetryLater(1.Seconds()));
            policy.Continuations[1].ShouldBe(new RetryLater(3.Seconds()));
            policy.Continuations[2].ShouldBe(new RetryLater(5.Seconds()));
        }

        [Fact]
        public void registering_retries_then_a_pause()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner(x => (x is CustomException {ErrorCode: 500}));

            var policy = settings.Policies.Single();

            policy.RetryLater(1.Seconds(), 3.Seconds(), 5.Seconds())
                .Then.Pause(1.Minutes());

            policy.Continuations.Count.ShouldBe(4);
            policy.Continuations[0].ShouldBe(new RetryLater(1.Seconds()));
            policy.Continuations[1].ShouldBe(new RetryLater(3.Seconds()));
            policy.Continuations[2].ShouldBe(new RetryLater(5.Seconds()));
            policy.Continuations[3].ShouldBe(new PauseProjection(1.Minutes()));
        }



        [Fact]
        public void registering_a_pause()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner(x => (x is CustomException {ErrorCode: 500}));

            var policy = settings.Policies.Single();
            policy.Pause(3.Seconds());

            policy.Continuations.Single().ShouldBe(new PauseProjection(3.Seconds()));
        }

        [Fact]
        public void registering_a_pause_all()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner(x => (x is CustomException {ErrorCode: 500}));

            var policy = settings.Policies.Single();
            policy.PauseAll(3.Seconds());

            policy.Continuations.Single().ShouldBe(new PauseAllProjections(3.Seconds()));
        }

        [Fact]
        public void registering_a_stop()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner(x => (x is CustomException {ErrorCode: 500}));

            var policy = settings.Policies.Single();
            policy.Stop();

            policy.Continuations.Single().ShouldBeOfType<StopProjection>();
        }

        [Fact]
        public void registering_a_stop_all()
        {
            var settings = new DaemonSettings();
            settings.OnException<InvalidOperationException>()
                .AndInner(x => (x is CustomException {ErrorCode: 500}));

            var policy = settings.Policies.Single();
            policy.StopAll();

            policy.Continuations.Single().ShouldBeOfType<StopAllProjections>();
        }




        public class CustomException: Exception
        {
            public int ErrorCode { get; }

            public CustomException(int errorCode)
            {
                ErrorCode = errorCode;
            }


        }
    }
}
