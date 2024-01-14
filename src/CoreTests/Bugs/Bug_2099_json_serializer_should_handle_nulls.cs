using Marten.Services.Json;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_2099_json_serializer_should_handle_nulls: BugIntegrationContext
{
    [Fact]
    public void serializing_null_with_systemtextjson_should_return_string_null()
    {
        StoreOptions(_ =>
        {
            _.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
        });

        TheStore.Serializer.ToJson(null).ShouldBe("null");
    }

    [Fact]
    public void serializing_null_with_newtonsoftjson_should_return_string_null()
    {
        StoreOptions(_ =>
        {
            _.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
        });

        TheStore.Serializer.ToJson(null).ShouldBe("null");
    }

    [Fact]
    public void serializing_null_with_jil_should_return_string_null()
    {
        StoreOptions(_ =>
        {
            _.Serializer<JilSerializer>();
        });

        TheStore.Serializer.ToJson(null).ShouldBe("null");
    }

}
