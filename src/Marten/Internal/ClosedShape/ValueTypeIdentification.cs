#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M15): <see cref="IIdentification{TDoc, TId}"/> for
/// strong-typed ids (Marten's <see cref="ValueTypeIdGeneration"/>). The
/// document's id member is a wrapper struct/class
/// (<typeparamref name="TWrapper"/>) over a primitive
/// (<typeparamref name="TInner"/>: Guid / int / long / string).
/// </summary>
/// <remarks>
/// Generation strategy depends on the inner type:
/// Guid -> <see cref="Guid.NewGuid"/>;
/// int / long -> per-document HiLo sequence;
/// string -> externally assigned (throws on missing).
/// Matches <c>ValueTypeIdGeneration.GenerateCode</c>.
/// </remarks>
public sealed class ValueTypeIdentification<TDoc, TWrapper, TInner>: IIdentification<TDoc, TWrapper>
    where TDoc : notnull
    where TWrapper : notnull
    where TInner : notnull
{
    private readonly Func<TDoc, TWrapper> _getter;
    private readonly Action<TDoc, TWrapper> _setter;
    private readonly Func<TWrapper, TInner> _unwrap;
    private readonly Func<TInner, TWrapper> _wrap;
    private readonly Func<IMartenDatabase, TInner> _generator;

    public ValueTypeIdentification(MemberInfo idMember, ValueTypeIdGeneration vt, Type sequenceKey)
    {
        _getter = LambdaBuilder.Getter<TDoc, TWrapper>(idMember);
        _setter = LambdaBuilder.Setter<TDoc, TWrapper>(idMember)!;
        _unwrap = LambdaBuilder.Getter<TWrapper, TInner>(vt.ValueProperty);
        _wrap = vt.CreateWrapper<TWrapper, TInner>();
        _generator = PickGenerator(vt.SimpleType, sequenceKey);
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
        var current = _getter(document);
        if (current is not null)
        {
            var inner = _unwrap(current);
            if (!EqualityComparer<TInner>.Default.Equals(inner, default!))
            {
                return current;
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
