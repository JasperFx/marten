using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.Parsing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// #5328: ReduceToConstant compiled a parameterless lambda with FastExpressionCompiler for every
// non-literal value in a where clause — a captured local, a compiled query's own property, a
// static member. FastExpressionCompiler is Reflection.Emit underneath, so under Native AOT the
// first such query threw
//
//     PlatformNotSupportedException: Dynamic code generation is not supported on this platform.
//
// The common shapes are now walked reflectively instead. These tests pin the shapes that must be
// evaluated WITHOUT emitting (the AOT contract), the shapes that must still fall through to FEC,
// and that the values that come out are the same either way.
public class Bug_5328_reduce_to_constant_without_emit: BugIntegrationContext
{
    private static readonly string StaticField = "static-field";
    private static string StaticProperty => "static-property";

    private readonly string _instanceField = "instance-field";

    [Fact]
    public void evaluates_a_constant_without_emitting()
    {
        LinqInternalExtensions.TryEvaluateWithoutCompiling(Expression.Constant("hello"), out var value)
            .ShouldBeTrue();
        value.ShouldBe("hello");
    }

    [Fact]
    public void evaluates_a_captured_local_without_emitting()
    {
        var captured = "captured-local";

        // The compiler turns `captured` into a field read on a display class, which is the single
        // most common shape reaching ReduceToConstant.
        Expression<Func<string>> expression = () => captured;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe("captured-local");
    }

    [Fact]
    public void evaluates_instance_and_static_members_without_emitting()
    {
        Expression<Func<string>> instance = () => _instanceField;
        LinqInternalExtensions.TryEvaluateWithoutCompiling(instance.Body, out var instanceValue).ShouldBeTrue();
        instanceValue.ShouldBe("instance-field");

        LinqInternalExtensions
            .TryEvaluateWithoutCompiling(Expression.Field(null, typeof(Bug_5328_reduce_to_constant_without_emit),
                nameof(StaticField)), out var staticFieldValue)
            .ShouldBeTrue();
        staticFieldValue.ShouldBe("static-field");

        Expression<Func<string>> staticProperty = () => StaticProperty;
        LinqInternalExtensions.TryEvaluateWithoutCompiling(staticProperty.Body, out var staticPropertyValue)
            .ShouldBeTrue();
        staticPropertyValue.ShouldBe("static-property");
    }

    [Fact]
    public void evaluates_a_nested_member_chain_without_emitting()
    {
        var holder = new Holder { Inner = new Inner { Name = "nested" } };
        Expression<Func<string>> expression = () => holder.Inner.Name;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe("nested");
    }

    [Fact]
    public void evaluates_a_boxing_conversion_without_emitting()
    {
        var number = 42;
        Expression<Func<object>> expression = () => number;

        // `() => number` where the return type is object wraps the field read in Convert.
        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public void evaluates_a_nullable_wrap_without_emitting()
    {
        var number = 42;
        Expression<Func<int?>> expression = () => number;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public void evaluates_an_array_literal_without_emitting()
    {
        var second = "b";
        Expression<Func<string[]>> expression = () => new[] { "a", second };

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe(new[] { "a", "b" });
    }

    [Fact]
    public void declines_a_numeric_conversion_so_the_value_is_never_reinterpreted_here()
    {
        var number = 42L;
        Expression<Func<int>> expression = () => (int)number;

        // A narrowing conversion changes the representation. Rather than reimplement the
        // conversion rules, hand it back to FastExpressionCompiler.
        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out _).ShouldBeFalse();
    }

    [Fact]
    public void declines_a_method_call()
    {
        Expression<Func<string>> expression = () => Guid.NewGuid().ToString();

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out _).ShouldBeFalse();
    }

    [Fact]
    public void the_shapes_it_declines_still_reduce_correctly_through_the_fallback()
    {
        var number = 42L;
        Expression<Func<int>> narrowing = () => (int)number;

        LinqInternalExtensions.ReduceToConstant(narrowing.Body).Value.ShouldBe(42);
    }

    [Fact]
    public async Task queries_with_captured_values_still_return_the_same_rows()
    {
        var target = new Bug5328Doc { Id = Guid.NewGuid(), Name = "Anne", Number = 7 };
        var other = new Bug5328Doc { Id = Guid.NewGuid(), Name = "Bob", Number = 9 };

        theSession.Store(target, other);
        await theSession.SaveChangesAsync();

        var name = "Anne";
        var number = 7;
        var names = new[] { "Anne", "Nobody" };
        var holder = new Holder { Inner = new Inner { Name = "Anne" } };

        (await theSession.Query<Bug5328Doc>().Where(x => x.Name == name).ToListAsync())
            .Single().Id.ShouldBe(target.Id);

        (await theSession.Query<Bug5328Doc>().Where(x => x.Number == number).ToListAsync())
            .Single().Id.ShouldBe(target.Id);

        (await theSession.Query<Bug5328Doc>().Where(x => x.Name.IsOneOf(names)).ToListAsync())
            .Single().Id.ShouldBe(target.Id);

        (await theSession.Query<Bug5328Doc>().Where(x => x.Name == holder.Inner.Name).ToListAsync())
            .Single().Id.ShouldBe(target.Id);
    }

    public class Holder
    {
        public Inner Inner { get; set; }
    }

    public class Inner
    {
        public string Name { get; set; }
    }
}

public class Bug5328Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public int Number { get; set; }
}
