using System.Reflection;
using System.Threading.Tasks;
using Npgsql;
using Npgsql.BackendMessages;
using Npgsql.PostgresTypes;
using Npgsql.TypeHandling;

namespace Marten.Schema.Identity.StronglyTyped
{
    /// <summary>
    /// This class is used by npgsql to read and write parameters that are strongly type ids. That is, it wraps the behaviour of the
    /// handler that would be used for the primitive type that the strongly typed id is "wrapping". 
    /// </summary>
    /// <remarks>
    /// Inspiration taken from the JsonHandler (in npgsql) as well as looking at the NpgsqlSimpleTypeHandler class, the implementation
    /// of which is not straight forward and the reason for why this code is not a simple forward of method calls to the underlying
    /// handler.
    ///
    /// We basically make use of the fact that the interface provides Read<TAny> and WriteWithLength<TAny> methods to enable
    /// mapping to any type
    /// </remarks>
    /// <typeparam name="TPrimitive"></typeparam>
    internal class WrappedPrimitiveHandler<TPrimitive>: NpgsqlTypeHandler<TPrimitive>
    {
        private readonly INpgsqlTypeHandler<TPrimitive> _primitiveTypeHandler;

        public WrappedPrimitiveHandler(PostgresType postgresType, INpgsqlTypeHandler<TPrimitive> primitiveTypeHandler) : base(postgresType)
        {
            _primitiveTypeHandler = primitiveTypeHandler;
        }

        public override ValueTask<TPrimitive> Read(NpgsqlReadBuffer buf, int len, bool async,
            FieldDescription? fieldDescription = null)
        {
            return this._primitiveTypeHandler.Read(buf, len, async, fieldDescription);
        }

        protected override async ValueTask<TAny> Read<TAny>(NpgsqlReadBuffer buf, int len, bool async, FieldDescription? fieldDescription = null)
        {
            var primitive = await this.Read(buf, len, async, fieldDescription);
            var primitiveAccessor = new DefaultWrappedPrimitiveAccessor<TAny, TPrimitive>();
            return primitiveAccessor.NewId(primitive);
        }

        public override Task Write(TPrimitive value, NpgsqlWriteBuffer buf, NpgsqlLengthCache? lengthCache, NpgsqlParameter? parameter,
            bool async)
        {
            return this.WriteUsingPrimitiveHandler(value, buf, lengthCache, parameter, async);
        }

        protected override Task WriteWithLength<TAny>(TAny value, NpgsqlWriteBuffer buf, NpgsqlLengthCache? lengthCache,
            NpgsqlParameter? parameter, bool async)
        {
            if (typeof(TAny) == typeof(TPrimitive))
            {
                return this.WriteUsingPrimitiveHandler((TPrimitive)(object)value, buf, lengthCache, parameter, async);
            }

            var primitiveAccessor = new DefaultWrappedPrimitiveAccessor<TAny, TPrimitive>();
            var primitive = primitiveAccessor.GetId(value);
            return this.WriteUsingPrimitiveHandler(primitive, buf, lengthCache, parameter, async);
        }

        protected override Task WriteObjectWithLength(object value, NpgsqlWriteBuffer buf, NpgsqlLengthCache? lengthCache,
            NpgsqlParameter? parameter, bool async)
        {
            return (Task)this.GetType().GetMethod(nameof(WriteWithLength), BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(value.GetType()).Invoke(this, new[] { value, buf, lengthCache, parameter, async });
        }

        private Task WriteUsingPrimitiveHandler(TPrimitive value, NpgsqlWriteBuffer buf, NpgsqlLengthCache? lengthCache,
            NpgsqlParameter? parameter,
            bool async)
        {
            if (this._primitiveTypeHandler is INpgsqlSimpleTypeHandler<TPrimitive> primitiveTypeHandler)
            {
                // see NpgsqlSimpleTypeHandler.WriteWithLengthInternal
                var elementLen = ValidateAndGetLength(value, ref lengthCache, parameter);
                if (buf.WriteSpaceLeft < 4 + elementLen)
                    return WriteWithLengthLong();
                buf.WriteInt32(elementLen);
                primitiveTypeHandler.Write(value, buf, parameter);
                return Task.CompletedTask;

                async Task WriteWithLengthLong()
                {
                    elementLen = ValidateAndGetLength(value, ref lengthCache, parameter);
                    if (buf.WriteSpaceLeft < 4 + elementLen)
                        await buf.Flush(async);
                    buf.WriteInt32(elementLen);
                    primitiveTypeHandler.Write(value, buf, parameter);
                }
            }

            return this._primitiveTypeHandler.Write(value, buf, lengthCache, parameter, async);
        }

        public override int ValidateAndGetLength(TPrimitive value, ref NpgsqlLengthCache? lengthCache,
            NpgsqlParameter? parameter)
            => this.ValidateAndGetLengthFromPrimitiveHandler(value, ref lengthCache, parameter);

        protected override int ValidateAndGetLength<TAny>(TAny value, ref NpgsqlLengthCache? lengthCache, NpgsqlParameter? parameter)
        {
            if (value is TPrimitive primitive)
            {
                return this.ValidateAndGetLengthFromPrimitiveHandler(primitive, ref lengthCache, parameter);
            }

            var primitiveAccessor = new DefaultWrappedPrimitiveAccessor<TAny, TPrimitive>();
            var wrappedPrimitive = primitiveAccessor.GetId(value);
            return this.ValidateAndGetLengthFromPrimitiveHandler(wrappedPrimitive, ref lengthCache, parameter);
        }

        protected override int ValidateObjectAndGetLength(object value, ref NpgsqlLengthCache? lengthCache,
            NpgsqlParameter? parameter)
        {
            return (int)this.GetType().GetMethod(nameof(ValidateAndGetLength), BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(value.GetType())
                .Invoke(this, new[]
                {
                    value, lengthCache, parameter
                });
        }

        private int ValidateAndGetLengthFromPrimitiveHandler(TPrimitive value, ref NpgsqlLengthCache? lengthCache,
            NpgsqlParameter? parameter)
        {
            // Simple type handlers throw NotSupportedException for the interface method and provide a different public method instead!!!
            // Breaks Liskov substitution principle if you ask me
            if (this._primitiveTypeHandler is INpgsqlSimpleTypeHandler<TPrimitive> simpleTypeHandler)
            {
                return simpleTypeHandler.ValidateAndGetLength(value, parameter);
            }

            return this._primitiveTypeHandler.ValidateAndGetLength(value, ref lengthCache, parameter);
        }
    }
}
