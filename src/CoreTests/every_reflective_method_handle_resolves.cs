using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5619. Marten caches a lot of <see cref="MethodInfo" /> handles in static fields, resolved by NAME
/// through <c>Type.GetMethod(...)</c>. That returns <c>null</c> for a member that is absent, and the house
/// style was <c>GetMethod(...)!</c> — whose <c>!</c> is erased at runtime, so the null simply flows on.
///
/// <para>
/// For the handles Marten <em>invokes</em>, #5619 replaced the lookup with <c>RequireMethod</c>, which
/// throws by name. But the larger group is the method-call parsers, which only ever <em>compare</em>
/// against a public, user-callable extension method:
/// </para>
///
/// <code>
/// public bool Matches(MethodCallExpression expression) => expression.Method == _method;
/// </code>
///
/// <para>
/// Those must NOT throw on a null. If the trimmer removed <c>SoftDeletedExtensions.IsDeleted</c>, it did so
/// because the application never calls it, so no expression tree can reference it and returning
/// <c>false</c> is correct — throwing would fail an app for not using soft deletes.
/// </para>
///
/// <para>
/// The failure those sites really have is a <b>refactor</b>: rename or re-overload one of those methods and
/// the lookup silently returns null, <c>Matches</c> silently stops matching, and the LINQ feature silently
/// dies with no error anywhere. A sibling store had three separate-looking LINQ bug reports that were all
/// that one line (JasperFx/polecat#741). A runtime check cannot tell that apart from legitimate trimming.
/// This test can, because <b>nothing is trimmed in a test run</b> — so a null here is a refactor, full stop.
/// </para>
/// </summary>
public class every_reflective_method_handle_resolves
{
    private readonly ITestOutputHelper _output;

    public every_reflective_method_handle_resolves(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void no_cached_method_handle_is_null()
    {
        // Handles that are deliberately optional, with the reason. A nullable MethodInfo field is a claim
        // that Marten works without it; anything not listed here is claiming the opposite.
        var deliberatelyOptional = new HashSet<string>
        {
            // #5619. Nothing calls TagTypeRegistration.Create except reflection, so a trimmer may remove it,
            // and the only consequence is that tag types are not auto-discovered. Failing the EventGraph
            // type initializer would take the whole store down for an app that trimmed a feature it does
            // not use.
            "Marten.Events.EventGraph.CreateTagTypeMethod"
        };

        var unresolved = new List<string>();
        var checkedCount = 0;

        foreach (var type in typeof(IDocumentStore).Assembly.GetTypes())
        {
            // An open generic has no static storage to read, and a type whose static state is per-closure
            // is covered by whichever closed instantiation the suite exercises.
            if (type.ContainsGenericParameters) continue;

            var fields = type
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                           BindingFlags.DeclaredOnly)
                .Where(x => x.FieldType == typeof(MethodInfo) || x.FieldType == typeof(MethodInfo[]))
                .ToArray();

            if (fields.Length == 0) continue;

            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (Exception e)
            {
                // A static constructor that needs configuration or a database is not this test's business,
                // but it should be visible rather than silently skipped -- a type that cannot initialise is
                // also a type whose handles were never checked.
                _output.WriteLine($"SKIPPED {type.FullNameInCode()}: static ctor threw {e.GetType().Name}");
                continue;
            }

            foreach (var field in fields)
            {
                checkedCount++;
                var value = field.GetValue(null);

                if (value == null)
                {
                    var name = $"{type.FullNameInCode()}.{field.Name}";
                    if (!deliberatelyOptional.Contains(name)) unresolved.Add(name);
                    continue;
                }

                if (value is MethodInfo[] array)
                {
                    for (var i = 0; i < array.Length; i++)
                    {
                        if (array[i] == null)
                        {
                            unresolved.Add($"{type.FullNameInCode()}.{field.Name}[{i}]");
                        }
                    }
                }
            }
        }

        // Guard against the test quietly measuring nothing -- if a refactor moves these handles off static
        // fields, the loop above finds none and passes for the wrong reason. Deliberately well below the ~31
        // found today: this is a floor against measuring nothing, not a census that every edit has to update.
        checkedCount.ShouldBeGreaterThan(20);

        _output.WriteLine($"Checked {checkedCount} cached MethodInfo handles.");

        unresolved.ShouldBeEmpty(
            "These reflective lookups resolved to null, which means the member was renamed or its signature "
            + "changed without the lookup being updated. Under a JIT nothing is trimmed, so this can only be "
            + "a refactor: " + unresolved.Join(", "));
    }
}
