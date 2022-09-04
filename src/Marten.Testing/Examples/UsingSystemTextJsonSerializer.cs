using Marten.Services;
using Marten.Services.Json;
using Weasel.Core;

namespace Marten.Testing.Examples;

public class UsingSystemTextJsonSerializer
{
    internal void using_stj()
    {
        #region sample_using_STJ_serialization

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Opt into System.Text.Json serialization
            opts.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            // Optionally configure the serializer directly
            opts.Serializer(new SystemTextJsonSerializer
            {
                // Optionally override the enum storage
                EnumStorage = EnumStorage.AsString,

                // Optionally override the member casing
                Casing = Casing.CamelCase
            });
        });

        #endregion
    }
}