#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using JasperFx.Core.Reflection;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M15): <see cref="IIdentification{TDoc, TId}"/> for
/// strong-typed ids — Vogen / StronglyTypedId
/// (<c>ValueTypeIdGeneration</c>) and F# single-case discriminated
/// unions (<c>FSharpDiscriminatedUnionIdGeneration</c>). The
/// document's id member is a wrapper struct/class
/// (<typeparamref name="TWrapper"/>) over a primitive
/// (<typeparamref name="TInner"/>: Guid / int / long / string).
/// </summary>
/// <remarks>
/// Generation strategy depends on the inner type:
/// Guid -> <see cref="Guid.NewGuid"/>;
/// int / long -> per-document HiLo sequence;
/// string -> externally assigned (throws on missing).
/// </remarks>
public sealed class ValueTypeIdentification<TDoc, TWrapper, TInner>: IIdentification<TDoc, TWrapper>
    where TDoc : notnull
    where TWrapper : notnull
    where TInner : notnull
{
    private readonly Func<TDoc, TWrapper> _getter;
    private readonly Func<TDoc, bool>? _isNullablePropertyMissing;
    private readonly Action<TDoc, TWrapper> _setter;
    private readonly Func<TWrapper, TInner> _unwrap;
    private readonly Func<TInner, TWrapper> _wrap;
    private readonly Func<IMartenDatabase, TInner> _generator;

    public ValueTypeIdentification(MemberInfo idMember, ValueTypeInfo vt, Type sequenceKey)
    {
        (_getter, _isNullablePropertyMissing) = BuildAccessors(idMember);
        _setter = LambdaBuilder.Setter<TDoc, TWrapper>(idMember)!;
        _unwrap = LambdaBuilder.Getter<TWrapper, TInner>(vt.ValueProperty);
        _wrap = vt.CreateWrapper<TWrapper, TInner>();
        _generator = PickGenerator(vt.SimpleType, sequenceKey);
    }

    /// <summary>
    /// Build the getter and an optional "is missing" predicate for the id
    /// member. When the document declares the id as
    /// <c>Nullable&lt;TWrapper&gt;</c>, <see cref="LambdaBuilder.Getter{T, M}"/>
    /// would emit <c>Expression.Convert</c> from <c>Nullable&lt;TWrapper&gt;</c>
    /// to <c>TWrapper</c>, which throws "Nullable object must have a value"
    /// when the property is null — the exact state for a freshly constructed
    /// document whose id hasn't been assigned. We can't substitute
    /// <c>default(TWrapper)</c> either: Vogen-generated wrappers throw
    /// "Use of uninitialized Value Object" on the inner-value accessor that
    /// <see cref="AssignIfMissing"/> calls next. So for nullable property
    /// types we return a separate <c>_isNullablePropertyMissing</c>
    /// predicate that lets <see cref="AssignIfMissing"/> short-circuit
    /// straight to id generation without touching the unset wrapper.
    /// </summary>
    private static (Func<TDoc, TWrapper> Getter, Func<TDoc, bool>? IsMissing) BuildAccessors(MemberInfo idMember)
    {
        var declaredType = idMember switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => throw new ArgumentException(
                $"Strong-typed id member must be a property or field; got {idMember.MemberType}.",
                nameof(idMember))
        };

        if (Nullable.GetUnderlyingType(declaredType) != typeof(TWrapper))
        {
            return (LambdaBuilder.Getter<TDoc, TWrapper>(idMember), null);
        }

        var target = Expression.Parameter(typeof(TDoc), "target");
        Expression access = idMember is PropertyInfo prop
            ? Expression.Property(target, prop)
            : Expression.Field(target, (FieldInfo)idMember);

        // .Value would throw for the null case, but AssignIfMissing now
        // guards every call with the predicate below, so this getter only
        // runs when HasValue is true.
        var unwrapNullable = Expression.Property(access, "Value");
        var getter = Expression.Lambda<Func<TDoc, TWrapper>>(unwrapNullable, target).CompileFast();

        var hasValue = Expression.Property(access, "HasValue");
        var isMissing = Expression.Not(hasValue);
        var predicate = Expression.Lambda<Func<TDoc, bool>>(isMissing, target).CompileFast();

        return (getter, predicate);
    }

    public TWrapper Identity(TDoc document) => _getter(document);

    public object ToRawSqlValue(TWrapper id) => _unwrap(id)!;

    public System.Type RawSqlType => typeof(TInner);

    public TWrapper ReadIdFromReader(System.Data.Common.DbDataReader reader, int columnOrdinal)
    {
        // Npgsql can't unbox a uuid / int / long / varchar column
        // directly into the wrapper type — read the inner primitive and
        // call the cached wrap delegate. Matches how the codegen path
        // emitted strong-typed id reads.
        var inner = reader.GetFieldValue<TInner>(columnOrdinal);
        return _wrap(inner);
    }

    public TWrapper AssignIfMissing(TDoc document, IMartenDatabase database)
    {
        // When the id member is Nullable<TWrapper> and the property is null,
        // skip straight to generation — calling _getter would either throw
        // "Nullable object must have a value" or hand out an uninitialized
        // Vogen wrapper that _unwrap would then reject.
        if (_isNullablePropertyMissing is null || !_isNullablePropertyMissing(document))
        {
            var current = _getter(document);
            if (current is not null)
            {
                var inner = _unwrap(current);
                if (!EqualityComparer<TInner>.Default.Equals(inner, default!))
                {
                    return current;
                }
            }
        }

        var newInner = _generator(database);
        var wrapped = _wrap(newInner);
        _setter(document, wrapped);
        return wrapped;
    }

    private static Func<IMartenDatabase, TInner> PickGenerator(Type simpleType, Type sequenceKey)
    {
        if (simpleType == typeof(Guid))
        {
            return _ => (TInner)(object)Guid.NewGuid();
        }
        if (simpleType == typeof(int))
        {
            return db => (TInner)(object)db.Sequences.SequenceFor(sequenceKey).NextInt();
        }
        if (simpleType == typeof(long))
        {
            return db => (TInner)(object)db.Sequences.SequenceFor(sequenceKey).NextLong();
        }
        if (simpleType == typeof(string))
        {
            return _ => throw new InvalidOperationException(
                "Strong-typed string ids are externally assigned — the caller must populate the id before saving.");
        }
        throw new NotSupportedException($"Strong-typed id inner type {simpleType.FullName} is not supported.");
    }
}
