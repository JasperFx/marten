using System;
using Npgsql;
using Npgsql.PostgresTypes;
using Npgsql.TypeHandlers;
using Npgsql.TypeHandling;

namespace Marten.Schema.Identity.StronglyTyped
{
    internal class WrappedPrimitiveTypeHandlerFactory<TPrimitive> : NpgsqlTypeHandlerFactory
    {
        private readonly NpgsqlTypeHandlerFactory primitiveTypeHandlerFactory;

        public WrappedPrimitiveTypeHandlerFactory(NpgsqlTypeHandlerFactory primitiveTypeHandlerFactory)
        {
            this.primitiveTypeHandlerFactory = primitiveTypeHandlerFactory;
        }

        public override NpgsqlTypeHandler CreateNonGeneric(PostgresType pgType, NpgsqlConnection conn)
        {
            var primitiveTypeHandler = this.primitiveTypeHandlerFactory.CreateNonGeneric(pgType, conn);
            if (primitiveTypeHandler is INpgsqlTypeHandler<TPrimitive> typedPrimitiveTypeHandler)
            {
                return new WrappedPrimitiveHandler<TPrimitive>(pgType, typedPrimitiveTypeHandler);
            }

            throw new NotImplementedException();
        }

        public override Type DefaultValueType => typeof(TPrimitive);
    }
}
