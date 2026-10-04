#nullable enable
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Marten.Schema.Identity;

/// <summary>
///     Builds the "read the inner primitive out of a strong-typed id wrapper" accessor shared by
///     <see cref="ValueTypeIdGeneration" /> and <see cref="FSharpDiscriminatedUnionIdGeneration" />.
/// </summary>
/// <remarks>
///     <para>
///         #5579. Both id strategies used to call FastExpressionCompiler's <c>CompileFast()</c>
///         unconditionally here. FEC is <c>Reflection.Emit</c> underneath, so in a Native AOT image this
///         threw <c>PlatformNotSupportedException</c> the first time anything read the inner value of a
///         strong-typed id — which is to say, on the first query that filtered or sorted by one.
///     </para>
///     <para>
///         JasperFx 2.80.0 (jasperfx#942) gave <c>ValueTypeInfo.CreateWrapper</c>/<c>UnWrapper</c> the same
///         treatment, but that fixed only the <i>wrapping</i> half. These two methods are Marten's own and
///         were left behind, so upgrading the package alone still left strong-typed ids broken under AOT.
///     </para>
///     <para>
///         The fallback is plain reflection rather than <c>Compile(preferInterpretation: true)</c>: the
///         expression being replaced is a single property getter, so there is nothing for an interpreter to
///         buy over <see cref="MethodBase.Invoke(object, object[])" />, and it matches what JasperFx 2.80
///         does on the wrapper side. FEC stays the default wherever it can run — this sits on the query
///         path and emitting is considerably faster than invoking reflectively per read.
///     </para>
/// </remarks>
internal static class StrongTypedIdValueSource
{
    public static Func<object, T> Build<T>(Type outerType, PropertyInfo valueProperty)
    {
        return Build<T>(outerType, valueProperty, RuntimeFeature.IsDynamicCodeSupported);
    }

    /// <summary>
    ///     Overload taking the platform capability explicitly so tests can exercise the reflective path on
    ///     a runtime that happens to support emitting.
    /// </summary>
    internal static Func<object, T> Build<T>(Type outerType, PropertyInfo valueProperty, bool canEmit)
    {
        var getter = valueProperty.GetMethod ??
                     throw new ArgumentOutOfRangeException(nameof(valueProperty),
                         $"{outerType.FullName}.{valueProperty.Name} has no getter, so the inner value of the identifier cannot be read.");

        if (!canEmit)
        {
            return target => (T)getter.Invoke(target, null)!;
        }

        var parameter = Expression.Parameter(typeof(object), "target");
        var callGetMethod = Expression.Call(Expression.Convert(parameter, outerType), getter);
        var lambda = Expression.Lambda<Func<object, T>>(callGetMethod, parameter);

        return FastExpressionCompiler.ExpressionCompiler.CompileFast(lambda);
    }
}
