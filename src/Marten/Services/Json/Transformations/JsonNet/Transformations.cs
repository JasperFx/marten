#nullable enable
using System;
using System.Data.Common;
using Marten.Exceptions;
using Newtonsoft.Json.Linq;

namespace Marten.Services.Json.Transformations.JsonNet
{
    public static class Transformations
    {
        public static JsonTransformation Upcast<TEvent>(Func<JObject, TEvent> transform)
            where TEvent : notnull
        {
            return new JsonTransformation(FromDbDataReader(transform));
        }

        public static Func<ISerializer, DbDataReader, int, object> FromDbDataReader<TEvent>(
            Func<JObject, TEvent> transform
        ) where TEvent : notnull
        {
            return (serializer, dbDataReader, index) =>
            {
                if (serializer is not JsonNetSerializer jsonNetSerializer)
                {
                    throw new MartenException(
                        $"Cannot use JsonNet upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(
                    jsonNetSerializer.JObjectFromJson(dbDataReader, index));
            };
        }
    }
}
