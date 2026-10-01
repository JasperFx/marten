using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// A captured Nullable<T> reaches ReduceToConstant as a property read on a display-class field:
// `captured.HasValue` or `captured.Value`. The reflective walk (#5328) evaluates the field first, and
// an empty Nullable<T> boxes to null, which leaves its getters without a target. Both members are read
// from the boxed value; the populated cases pin that the same path still returns the right values.
public class captured_nullable_members_in_where: BugIntegrationContext
{
    [Fact]
    public void has_value_of_an_empty_captured_nullable_is_false()
    {
        Guid? captured = null;
        Expression<Func<bool>> expression = () => captured.HasValue;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe(false);
    }

    [Fact]
    public void has_value_and_value_of_a_populated_captured_nullable()
    {
        Guid? captured = Guid.NewGuid();
        Expression<Func<bool>> hasValue = () => captured.HasValue;
        Expression<Func<Guid>> valueRead = () => captured.Value;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(hasValue.Body, out var has).ShouldBeTrue();
        has.ShouldBe(true);

        LinqInternalExtensions.TryEvaluateWithoutCompiling(valueRead.Body, out var value).ShouldBeTrue();
        value.ShouldBe(captured.Value);
    }

    [Fact]
    public void value_of_an_empty_captured_nullable_is_still_a_bad_linq_expression()
    {
        Guid? captured = null;
        Expression<Func<Guid>> expression = () => captured.Value;

        Should.Throw<BadLinqExpressionException>(() => expression.Body.ReduceToConstant());
    }

    [Fact]
    public async Task query_with_an_empty_captured_nullable()
    {
        var first = new CapturedNullableDoc { Id = Guid.NewGuid() };
        var second = new CapturedNullableDoc { Id = Guid.NewGuid() };
        theSession.Store(first, second);
        await theSession.SaveChangesAsync();

        Guid? excluded = null;
        var results = await theSession.Query<CapturedNullableDoc>()
            .Where(x => !excluded.HasValue || x.Id != excluded)
            .ToListAsync();

        results.Select(x => x.Id).ShouldContain(first.Id);
        results.Select(x => x.Id).ShouldContain(second.Id);
    }

    [Fact]
    public async Task query_with_a_populated_captured_nullable()
    {
        var first = new CapturedNullableDoc { Id = Guid.NewGuid() };
        var second = new CapturedNullableDoc { Id = Guid.NewGuid() };
        theSession.Store(first, second);
        await theSession.SaveChangesAsync();

        Guid? excluded = first.Id;
        var results = await theSession.Query<CapturedNullableDoc>()
            .Where(x => (x.Id == first.Id || x.Id == second.Id) && (!excluded.HasValue || x.Id != excluded.Value))
            .ToListAsync();

        results.Select(x => x.Id).ShouldBe([second.Id]);
    }
}

public class CapturedNullableDoc
{
    public Guid Id { get; set; }
}
