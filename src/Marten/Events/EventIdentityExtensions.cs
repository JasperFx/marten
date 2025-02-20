#nullable enable
using System;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Internal;

namespace Marten.Events;

internal static class EventIdentityExtensions
{
    public static TId IdentityFromEvent<TId>(this IEvent e, StreamIdentity streamIdentity)
    {
        if (streamIdentity == StreamIdentity.AsGuid)
        {
            if (typeof(TId) == typeof(Guid))
            {
                return e.StreamId.As<TId>();
            }

            var valueTypeInfo = new StoreOptions().RegisterValueType(typeof(TId));
            return valueTypeInfo.CreateAggregateIdentitySource<TId>()(e);
        }
        else
        {
            if (typeof(TId) == typeof(string))
            {
                return e.StreamKey.As<TId>();
            }

            var valueTypeInfo = new StoreOptions().RegisterValueType(typeof(TId));
            return valueTypeInfo.CreateAggregateIdentitySource<TId>()(e);
        }
    }
}
