using System;
using Npgsql.TypeHandling;

namespace Marten.Schema.Identity.StronglyTyped
{
    internal static class NpgsqlTypeHandlerFactoryExtensions
    {
        public static NpgsqlTypeHandlerFactory ToWrappedPrimitiveHandlerFactory(this NpgsqlTypeHandlerFactory primitiveTypeHandlerFactory, Type primitiveType)
        {
            return (NpgsqlTypeHandlerFactory) Activator.CreateInstance(typeof(WrappedPrimitiveTypeHandlerFactory<>).MakeGenericType(primitiveType),
                primitiveTypeHandlerFactory);
        }
    }
}
