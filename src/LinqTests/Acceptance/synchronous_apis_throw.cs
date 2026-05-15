using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

public class synchronous_apis_throw: IntegrationContext
{
    public synchronous_apis_throw(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public void ToList_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().ToList());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void ToArray_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().ToArray());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void First_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().First());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void FirstOrDefault_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().FirstOrDefault());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void Single_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().Single());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void SingleOrDefault_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().SingleOrDefault());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void Count_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().Count());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void LongCount_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().LongCount());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void Any_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().Any());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void Min_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<Target>().Min(x => x.Number));
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void Max_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<Target>().Max(x => x.Number));
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void Sum_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<Target>().Sum(x => x.Number));
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void Average_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<Target>().Average(x => x.Number));
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void ToDictionary_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() => theSession.Query<User>().ToDictionary(x => x.Id));
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void foreach_iteration_throws_NotSupportedException()
    {
        var ex = Should.Throw<NotSupportedException>(() =>
        {
            foreach (var _ in theSession.Query<User>())
            {
                // never reached
            }
        });
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void non_generic_enumerator_throws_NotSupportedException()
    {
        var queryable = theSession.Query<User>();
        var ex = Should.Throw<NotSupportedException>(() => ((IEnumerable)queryable).GetEnumerator());
        ex.Message.ShouldBe(QuerySession.SynchronousNotSupportedMessage);
    }

    [Fact]
    public void exception_message_references_marten_9()
    {
        QuerySession.SynchronousNotSupportedMessage
            .ShouldBe("As of Marten 9.0, only asynchronous data access is supported");
    }
}
