#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Exceptions;
using Weasel.Storage;

namespace Marten.Linq.Members.Unions;

/// <summary>
///     #5735: how each case of a C# union-typed document member is recognised in the stored JSON, as the
///     <see cref="UnionCaseTest" /> that Weasel's <see cref="SqlServerUnionSqlDialect" /> renders.
/// </summary>
/// <remarks>
///     <para>
///         System.Text.Json writes a union as its bare active case: no wrapper and no discriminator
///         (jasperfx#1037). So a case is recognised only by what its JSON looks like. A primitive case by
///         its JSON kind, which no other case may share; an object case by the properties it always writes
///         and the properties only the other object cases write.
///     </para>
///     <para>
///         ⚠️ <b>Only properties that are always written can be required.</b> A serializer configured with
///         <see cref="JsonIgnoreCondition.WhenWritingNull" /> leaves a null property out, so a nullable property
///         can be missing from a row of its own case. A property counts as always written when it is a non-nullable value
///         type (and the options do not skip defaults), or a reference type annotated as not null. A case
///         that cannot be told from another one this way is refused, naming both, rather than translated
///         into a test that matches rows of the other case.
///     </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "Reads JsonTypeInfo for union case types from the store's own serializer options; document types and their members are preserved at registration per the AOT publishing guide.")]
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "Reads JsonTypeInfo for union case types from the store's own serializer options; document types and their members are preserved at registration per the AOT publishing guide.")]
[UnconditionalSuppressMessage("Trimming", "IL2072:DynamicallyAccessedMembers",
    Justification = "Case types come from the union's own public constructors, which event and document registration preserve.")]
internal sealed class UnionShape
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<(Type, bool), UnionShape>>
        Shapes = new();

    private readonly Dictionary<Type, UnionCaseTest> _tests = new();
    private readonly Dictionary<Type, string> _refusals = new();

    private UnionShape(Type unionType, IReadOnlyList<Type> caseTypes)
    {
        UnionType = unionType;
        CaseTypes = caseTypes;
    }

    public Type UnionType { get; }
    public IReadOnlyList<Type> CaseTypes { get; }

    public static bool IsUnionValue(MemberExpression expression)
        => expression.Member.Name == "Value" && expression.Expression is { } owner && owner.Type.IsUnion();

    public static UnionShape For(Type unionType, JsonSerializerOptions options, bool enumsAsStrings)
    {
        var shapes = Shapes.GetValue(options, static _ => new ConcurrentDictionary<(Type, bool), UnionShape>());
        return shapes.GetOrAdd((unionType, enumsAsStrings), key => Build(key.Item1, options, key.Item2));
    }

    /// <summary>
    ///     The test for <paramref name="caseType" />, which must be one of the cases.
    /// </summary>
    /// <exception cref="BadLinqExpressionException">The case cannot be told from another one.</exception>
    public UnionCaseTest TestFor(Type caseType, string expression)
    {
        if (_tests.TryGetValue(caseType, out var test)) return test;

        throw new BadLinqExpressionException(
            $"Marten cannot translate '{expression}' to SQL: {_refusals[caseType]} System.Text.Json writes a "
            + $"union as its bare case with no discriminator, so the stored JSON is all there is to tell "
            + $"{UnionType.ShortNameInCode()}'s cases apart. Give the case types distinct property names (or "
            + "a discriminator property of their own), or materialize the query and test the case in memory.");
    }

    /// <summary>
    ///     The cases a type test against <paramref name="tested" /> stands for: the case itself, or every
    ///     case assignable to it.
    /// </summary>
    public IReadOnlyList<Type> CasesMatching(Type tested)
    {
        var underlying = Nullable.GetUnderlyingType(tested) ?? tested;
        return CaseTypes.Where(x => x == underlying || underlying.IsAssignableFrom(x)).ToArray();
    }

    private static UnionShape Build(Type unionType, JsonSerializerOptions options, bool enumsAsStrings)
    {
        // GetTypeInfo needs the options read-only. These are the serializer's live write options, which a
        // later Configure() call may still change, so describe the cases through a read-only copy instead.
        if (!options.IsReadOnly)
        {
            options = new JsonSerializerOptions(options);
            options.MakeReadOnly(populateMissingResolver: true);
        }

        var shape = new UnionShape(unionType, unionType.CaseTypes());
        var described = shape.CaseTypes.Select(x => Describe(x, options, enumsAsStrings)).ToArray();

        foreach (var current in described)
        {
            if (current.Refusal != null)
            {
                shape._refusals[current.CaseType] = current.Refusal;
                continue;
            }

            var kind = current.Kind!.Value;
            var others = described.Where(x => x != current).ToArray();

            if (kind != UnionCaseJsonKind.Object)
            {
                var clash = others.FirstOrDefault(x => x.Kind == kind || x.Refusal != null);
                if (clash != null)
                {
                    shape._refusals[current.CaseType] = clash.Kind == kind
                        ? $"the cases {current.CaseType.ShortNameInCode()} and {clash.CaseType.ShortNameInCode()} are both written as a JSON {kind}."
                        : $"the case {clash.CaseType.ShortNameInCode()} is written in a shape Marten cannot recognise, so {current.CaseType.ShortNameInCode()} cannot be told from it.";
                    continue;
                }

                shape._tests[current.CaseType] = UnionCaseTest.OfKind(kind);
                continue;
            }

            var objects = others.Where(x => x.Kind == UnionCaseJsonKind.Object || x.Refusal != null).ToArray();
            if (objects.Length == 0)
            {
                shape._tests[current.CaseType] = UnionCaseTest.OfKind(UnionCaseJsonKind.Object);
                continue;
            }

            string? refusal = null;
            foreach (var other in objects)
            {
                if (current.IsDictionary || other.IsDictionary || other.Refusal != null)
                {
                    refusal = $"the cases {current.CaseType.ShortNameInCode()} and {other.CaseType.ShortNameInCode()} are both written as JSON objects, and one of them has no fixed set of property names.";
                    break;
                }

                // A row of `other` fails this case's test when it always writes a property this case never
                // writes (absent), or when this case always writes one `other` never writes (required).
                var distinguishable = other.AlwaysWritten.Any(x => !current.AllProperties.Contains(x))
                                      || current.AlwaysWritten.Any(x => !other.AllProperties.Contains(x));
                if (!distinguishable)
                {
                    refusal = $"a stored {other.CaseType.ShortNameInCode()} can look exactly like a {current.CaseType.ShortNameInCode()}: neither case always writes a property the other never writes (a nullable property can be left out when it is null).";
                    break;
                }
            }

            if (refusal != null)
            {
                shape._refusals[current.CaseType] = refusal;
                continue;
            }

            var absent = objects.SelectMany(x => x.AllProperties).Where(x => !current.AllProperties.Contains(x))
                .Distinct(StringComparer.Ordinal).ToArray();

            shape._tests[current.CaseType] = UnionCaseTest.ObjectWith(current.AlwaysWritten.ToArray(), absent);
        }

        return shape;
    }

    private static CaseDescription Describe(Type caseType, JsonSerializerOptions options, bool enumsAsStrings)
    {
        if (caseType.IsUnion())
        {
            return CaseDescription.Refused(caseType,
                $"the case {caseType.ShortNameInCode()} is itself a union, and a nested union case is not translated.");
        }

        var underlying = Nullable.GetUnderlyingType(caseType) ?? caseType;
        JsonTypeInfo info;
        try
        {
            info = options.GetTypeInfo(underlying);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
        {
            return CaseDescription.Refused(caseType,
                $"the serializer cannot describe the case {caseType.ShortNameInCode()} ({e.Message}).");
        }

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Enumerable:
                return CaseDescription.OfKind(caseType, UnionCaseJsonKind.Array);
            case JsonTypeInfoKind.Dictionary:
                return new CaseDescription(caseType, UnionCaseJsonKind.Object, null, new HashSet<string>(), new HashSet<string>(), IsDictionary: true);
            case JsonTypeInfoKind.Object:
                return DescribeObject(caseType, info, options);
        }

        var kind = PrimitiveKind(underlying, enumsAsStrings);
        return kind == null
            ? CaseDescription.Refused(caseType,
                $"the case {caseType.ShortNameInCode()} is written by a converter, so Marten cannot tell what JSON kind it takes.")
            : CaseDescription.OfKind(caseType, kind.Value);
    }

    private static CaseDescription DescribeObject(Type caseType, JsonTypeInfo info, JsonSerializerOptions options)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        var always = new HashSet<string>(StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();

        foreach (var property in info.Properties)
        {
            if (property.Get == null) continue;
            all.Add(property.Name);

            if (IsAlwaysWritten(property, options, nullability)) always.Add(property.Name);
        }

        return new CaseDescription(caseType, UnionCaseJsonKind.Object, null, all, always, IsDictionary: false);
    }

    private static bool IsAlwaysWritten(JsonPropertyInfo property, JsonSerializerOptions options,
        NullabilityInfoContext nullability)
    {
        if (property.ShouldSerialize != null) return false;

        var ignore = property.AttributeProvider is MemberInfo member
            ? member.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition ?? options.DefaultIgnoreCondition
            : options.DefaultIgnoreCondition;

        switch (ignore)
        {
            case JsonIgnoreCondition.Never:
                return true;
            case JsonIgnoreCondition.WhenWritingNull:
            case JsonIgnoreCondition.WhenWritingDefault:
                break;
            default:
                // Always, WhenWriting, WhenReading: never written, or not worth guessing about.
                return ignore == JsonIgnoreCondition.WhenReading;
        }

        var type = property.PropertyType;
        if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
        {
            // A value type is never null, but WhenWritingDefault leaves out a 0 or a false.
            return ignore == JsonIgnoreCondition.WhenWritingNull;
        }

        // A null reference is left out, so the property can only be required when it is declared not null.
        var state = property.AttributeProvider switch
        {
            PropertyInfo p => nullability.Create(p).ReadState,
            FieldInfo f => nullability.Create(f).ReadState,
            _ => NullabilityState.Unknown
        };

        return state == NullabilityState.NotNull;
    }

    private static UnionCaseJsonKind? PrimitiveKind(Type type, bool enumsAsStrings)
    {
        if (type == typeof(bool)) return UnionCaseJsonKind.Boolean;
        if (type.IsEnum) return enumsAsStrings ? UnionCaseJsonKind.String : UnionCaseJsonKind.Number;

        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly)
            || type == typeof(TimeSpan) || type == typeof(Uri) || type == typeof(byte[]))
        {
            return UnionCaseJsonKind.String;
        }

        if (type == typeof(decimal) || type == typeof(Half) || type == typeof(Int128) || type == typeof(UInt128)
            || (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr)))
        {
            return UnionCaseJsonKind.Number;
        }

        return null;
    }

    private sealed record CaseDescription(
        Type CaseType,
        UnionCaseJsonKind? Kind,
        string? Refusal,
        IReadOnlySet<string> AllProperties,
        IReadOnlySet<string> AlwaysWritten,
        bool IsDictionary)
    {
        public static CaseDescription OfKind(Type caseType, UnionCaseJsonKind kind)
            => new(caseType, kind, null, new HashSet<string>(), new HashSet<string>(), false);

        public static CaseDescription Refused(Type caseType, string why)
            => new(caseType, null, why, new HashSet<string>(), new HashSet<string>(), false);
    }
}
