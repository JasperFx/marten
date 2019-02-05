using System;
using System.Collections.Generic;
using System.IO;
using Baseline;
using Marten;
using Marten.Services;

namespace MartenBenchmarks.BenchAgainst
{
    public class IdentityMapBaseline : IdentityMapWithConcurrentDictionary<object>
    {
        public IdentityMapBaseline(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
            : base(serializer, listeners)
        {
        }

        protected override object ToCache(object id, Type concreteType, object document, TextReader json,
            UnitOfWorkOrigin origin = UnitOfWorkOrigin.Loaded)
        {
            return document;
        }

        protected override T FromCache<T>(object cacheValue)
        {
            return cacheValue.As<T>();
        }
    }
}