using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.Parsing;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace LinqTests.Bugs;

// #5361: #5328 gave ReduceToConstant a reflective walk for the closed expression shapes Marten sees
// most often, so a Native AOT binary would not hit Reflection.Emit on every `where x.Member ==
// captured`. Enum equality was not one of those shapes. The C# compiler lowers
//
//     x.Soort == captured
//
// to Equal(Convert(x.Soort, Int32), Convert(closure.captured, Int32)), and the enum-to-underlying
// Convert fell through to FastExpressionCompiler — so the comparison threw
// PlatformNotSupportedException under AOT while the same comparison against a literal worked.
//
// Two changes, pinned here: the enum Convert is now walked reflectively like the rest, and the
// shapes that do still need a compiled lambda fall back to the BCL expression interpreter instead
// of hard-failing wherever the platform cannot emit.
public class Bug_5361_enum_compared_to_a_variable: BugIntegrationContext
{
    [Fact]
    public void evaluates_an_enum_to_underlying_convert_without_emitting()
    {
        var captured = Bug5361Soort.Apotheek;
        Expression<Func<Bug5361Doc, bool>> expression = x => x.Soort == captured;

        // The shape the compiler actually hands the parser.
        var right = ((BinaryExpression)expression.Body).Right;
        right.NodeType.ShouldBe(ExpressionType.Convert);
        right.Type.ShouldBe(typeof(int));

        LinqInternalExtensions.TryEvaluateWithoutCompiling(right, out var value).ShouldBeTrue();
        value.ShouldBe(1);
    }

    [Fact]
    public void evaluates_a_nullable_enum_convert_without_emitting()
    {
        Bug5361Soort? captured = Bug5361Soort.Apotheek;
        Expression<Func<Bug5361Doc, bool>> expression = x => x.OptioneleSoort == captured;

        var right = ((BinaryExpression)expression.Body).Right;
        right.Type.ShouldBe(typeof(int?));

        LinqInternalExtensions.TryEvaluateWithoutCompiling(right, out var value).ShouldBeTrue();
        value.ShouldBe(1);
    }

    [Fact]
    public void evaluates_a_null_nullable_enum_convert_without_emitting()
    {
        Bug5361Soort? captured = null;
        Expression<Func<Bug5361Doc, bool>> expression = x => x.OptioneleSoort == captured;

        var right = ((BinaryExpression)expression.Body).Right;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(right, out var value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    [Fact]
    public void evaluates_an_underlying_to_enum_cast_without_emitting()
    {
        var captured = 1;
        Expression<Func<Bug5361Soort>> expression = () => (Bug5361Soort)captured;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe(Bug5361Soort.Apotheek);
    }

    [Fact]
    public void evaluates_a_cast_between_two_enums_sharing_an_underlying_type_without_emitting()
    {
        var captured = Bug5361Soort.Apotheek;
        Expression<Func<Bug5361Ander>> expression = () => (Bug5361Ander)captured;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe(Bug5361Ander.Tweede);
    }

    [Fact]
    public void evaluates_an_enum_whose_underlying_type_is_not_int_without_emitting()
    {
        var captured = Bug5361Byte.Tweede;
        Expression<Func<byte>> expression = () => (byte)captured;

        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out var value).ShouldBeTrue();
        value.ShouldBe((byte)1);
    }

    [Fact]
    public void still_declines_a_widening_conversion_out_of_an_enum()
    {
        var captured = Bug5361Soort.Apotheek;
        Expression<Func<long>> expression = () => (long)captured;

        // int -> long changes the representation, so it stays with the compiled path rather than
        // being reinterpreted here. Only conversions that share one integral representation qualify.
        LinqInternalExtensions.TryEvaluateWithoutCompiling(expression.Body, out _).ShouldBeFalse();

        // ...and still reduces to the right value through that path.
        LinqInternalExtensions.ReduceToConstant(expression.Body).Value.ShouldBe(1L);
    }

    [Fact]
    public void the_interpreted_fallback_reads_the_same_value_as_the_emitted_one()
    {
        var captured = Bug5361Soort.Apotheek;
        Expression<Func<long>> widening = () => (long)captured;
        var lambda = Expression.Lambda<Func<object>>(Expression.Convert(widening.Body, typeof(object)));

        // This is the branch a Native AOT binary takes: no Reflection.Emit anywhere.
        LinqInternalExtensions.CompileValueReader(lambda, canEmit: false)()
            .ShouldBe(LinqInternalExtensions.CompileValueReader(lambda, canEmit: true)());
    }

    [Fact]
    public async Task queries_an_enum_against_a_captured_variable()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5361Doc));

        var huisarts = new Bug5361Doc { Soort = Bug5361Soort.Huisarts, OptioneleSoort = Bug5361Soort.Huisarts };
        var apotheek = new Bug5361Doc { Soort = Bug5361Soort.Apotheek };

        theSession.Store(huisarts, apotheek);
        await theSession.SaveChangesAsync();

        var soort = Bug5361Soort.Huisarts;

        (await theSession.Query<Bug5361Doc>().Where(x => x.Soort == soort).ToListAsync())
            .Single().Id.ShouldBe(huisarts.Id);

        (await theSession.Query<Bug5361Doc>().Where(x => x.Soort != soort).ToListAsync())
            .Single().Id.ShouldBe(apotheek.Id);

        (await theSession.Query<Bug5361Doc>().Where(x => x.Soort > soort).ToListAsync())
            .Single().Id.ShouldBe(apotheek.Id);

        Bug5361Soort? optioneel = Bug5361Soort.Huisarts;
        (await theSession.Query<Bug5361Doc>().Where(x => x.OptioneleSoort == optioneel).ToListAsync())
            .Single().Id.ShouldBe(huisarts.Id);

        // The same rows the literal form returns, which is what made the failure so surprising in
        // the field: one of these two spellings worked and the other did not.
        (await theSession.Query<Bug5361Doc>().Where(x => x.Soort == Bug5361Soort.Huisarts).ToListAsync())
            .Single().Id.ShouldBe(huisarts.Id);
    }

    [Fact]
    public async Task queries_an_enum_against_a_captured_variable_when_stored_as_a_string()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsString));
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5361Doc));

        var huisarts = new Bug5361Doc { Soort = Bug5361Soort.Huisarts };
        var apotheek = new Bug5361Doc { Soort = Bug5361Soort.Apotheek };

        theSession.Store(huisarts, apotheek);
        await theSession.SaveChangesAsync();

        var soort = Bug5361Soort.Huisarts;

        // The reduced constant is the integral value either way; turning it back into a name is
        // EnumAsStringMember's job, so the storage mode must not change what comes back.
        (await theSession.Query<Bug5361Doc>().Where(x => x.Soort == soort).ToListAsync())
            .Single().Id.ShouldBe(huisarts.Id);

        (await theSession.Query<Bug5361Doc>().Where(x => x.Soort != soort).ToListAsync())
            .Single().Id.ShouldBe(apotheek.Id);
    }
}

public class Bug5361Doc
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Bug5361Soort Soort { get; set; }
    public Bug5361Soort? OptioneleSoort { get; set; }
}

public enum Bug5361Soort
{
    Huisarts,
    Apotheek
}

public enum Bug5361Ander
{
    Eerste,
    Tweede
}

public enum Bug5361Byte: byte
{
    Eerste,
    Tweede
}
