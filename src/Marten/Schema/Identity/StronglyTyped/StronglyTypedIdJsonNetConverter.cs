using System;
using Newtonsoft.Json;

namespace Marten.Schema.Identity.StronglyTyped
{
    internal class StronglyTypedIdJsonNetConverter<TId, TPrimitive>: Newtonsoft.Json.JsonConverter<TId>
    {
        private DefaultWrappedPrimitiveAccessor<TId, TPrimitive> wrappedPrimitiveAccessor;

        public StronglyTypedIdJsonNetConverter()
        {
            this.wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<TId, TPrimitive>();
        }

        public override void WriteJson(JsonWriter writer, TId value, JsonSerializer serializer)
        {
            var primitiveId = this.wrappedPrimitiveAccessor.GetId(value);
            serializer.Serialize(writer, primitiveId);
        }

        public override TId ReadJson(JsonReader reader, Type objectType, TId existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var id = serializer.Deserialize<TPrimitive>(reader);
            return this.wrappedPrimitiveAccessor.NewId(id);
        }
    }
}
