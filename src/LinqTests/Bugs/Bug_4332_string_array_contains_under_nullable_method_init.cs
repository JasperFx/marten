#nullable enable
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_4332_string_array_contains_under_nullable_method_init: BugIntegrationContext
{
    private static string[] MakeNames(params string[] names) => names;

    // Reproduces https://github.com/JasperFx/marten/issues/4332.
    //
    // Under C# 14 with <Nullable>enable</Nullable>, when a string[] local is
    // initialised from a method-typed return (var v = MakeNames(...);), the
    // compiler emits the Where predicate with an extra Convert() wrapper
    // around the captured closure field:
    //
    //     s => op_Implicit(Convert(closureField, String[])).Contains(s.UserName)
    //
    // MemoryExtensionsContains.UnwrapConversions only peels op_Implicit, so
    // the receiver fails to reduce to a constant and the parser falls through
    // to ValueCollectionMember.ParseWhereForContains, which CompileFasts the
    // whole lambda and throws InvalidOperationException about 's' not being
    // defined in scope.
    [Fact]
    public async Task can_query_when_string_array_local_is_method_initialised()
    {
        theSession.Store(new User { UserName = "alice" });
        theSession.Store(new User { UserName = "bob" });
        await theSession.SaveChangesAsync();

        var values = MakeNames("alice");

        var results = await theSession.Query<User>()
            .Where(u => values.Contains(u.UserName))
            .ToListAsync();

        results.Count.ShouldBe(1);
        results[0].UserName.ShouldBe("alice");
    }

    // Same scenario, but the failing expression shape is constructed by hand
    // so the bug reproduces deterministically regardless of which target
    // framework / C# language version the test project was built against.
    [Fact]
    public async Task can_query_when_collection_expression_has_convert_wrapper()
    {
        theSession.Store(new User { UserName = "alice" });
        theSession.Store(new User { UserName = "bob" });
        await theSession.SaveChangesAsync();

        var holder = new ValuesHolder { Values = new[] { "alice" } };
        var lambda = BuildPredicateWithConvertWrapper(holder);

        var results = await theSession.Query<User>().Where(lambda).ToListAsync();

        results.Count.ShouldBe(1);
        results[0].UserName.ShouldBe("alice");
    }

    private static Expression<Func<User, bool>> BuildPredicateWithConvertWrapper(ValuesHolder holder)
    {
        var s = Expression.Parameter(typeof(User), "s");

        // The captured-closure field, then a Convert() wrapper to string[]:
        //   Convert(holder.Values, String[])
        var fieldAccess = Expression.Field(Expression.Constant(holder), nameof(ValuesHolder.Values));
        var converted = Expression.Convert(fieldAccess, typeof(string[]));

        // Implicit string[] -> ReadOnlySpan<string>, exactly what C# emits.
        var opImplicit = typeof(ReadOnlySpan<string>).GetMethod(
            "op_Implicit",
            new[] { typeof(string[]) })!;
        var asSpan = Expression.Call(opImplicit, converted);

        // MemoryExtensions.Contains<string>(ReadOnlySpan<string>, string)
        var containsOpen = typeof(MemoryExtensions).GetMethods()
            .Single(m => m.Name == "Contains"
                         && m.IsGenericMethodDefinition
                         && m.GetParameters().Length == 2
                         && m.GetParameters()[0].ParameterType.IsGenericType
                         && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition()
                             == typeof(ReadOnlySpan<>));
        var contains = containsOpen.MakeGenericMethod(typeof(string));

        var nameMember = Expression.Property(s, nameof(User.UserName));
        var body = Expression.Call(contains, asSpan, nameMember);
        return Expression.Lambda<Func<User, bool>>(body, s);
    }

    private class ValuesHolder
    {
        public string[] Values = Array.Empty<string>();
    }
}
