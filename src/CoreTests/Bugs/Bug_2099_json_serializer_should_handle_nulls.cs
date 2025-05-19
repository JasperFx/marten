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
            _.UseSystemTextJsonForSerialization();
        });

        theStore.Serializer.ToJson(null).ShouldBe("null");
    }

    [Fact]
    public void serializing_null_with_newtonsoftjson_should_return_string_null()
    {
        StoreOptions(_ =>
        {
            _.UseNewtonsoftForSerialization();
        });

        theStore.Serializer.ToJson(null).ShouldBe("null");
    }

}
