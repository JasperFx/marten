using System.Numerics;
using Marten.Services.Json;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class BigIntegerTests : BugIntegrationContext
{
    private static readonly string LargerThanLongValue = "123456789012345678901234567890123456789012345678901234567890";

    [Fact]
    public void When_Querying_Using_Newtonsoft_Json_Should_Persist_And_Fetch_BigInteger_Values()
    {
        StoreOptions(options =>
        {
            options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

            options.Schema.For<BigIntegerObject>()
                .Duplicate(x => x.Value);
        });

        using (var session = theStore.LightweightSession())
        {
            var obj = new BigIntegerObject { Id = 1, Value = BigInteger.Parse(LargerThanLongValue) };

            session.Store(obj);
            session.SaveChanges();
        }

        using (var session = theStore.QuerySession())
        {
            var result = session.Load<BigIntegerObject>(1);

            result.Value.ToString().ShouldBe(LargerThanLongValue);
        }
    }

    [Fact]
    public void When_Querying_Using_SystemTextJson_Should_Persist_And_Fetch_BigInteger_Values()
    {
        StoreOptions(options =>
        {
            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.Schema.For<BigIntegerObject>()
                .Duplicate(x => x.Value);
        });

        using (var session = theStore.LightweightSession())
        {
            var obj = new BigIntegerObject { Id = 1, Value = BigInteger.Parse(LargerThanLongValue) };

            session.Store(obj);
            session.SaveChanges();
        }

        using (var session = theStore.QuerySession())
        {
            var result = session.Load<BigIntegerObject>(1);

            result.Value.ToString().ShouldBe(LargerThanLongValue);
        }
    }

    [Fact]
    public void When_Stored_With_Newtonsoft_And_Fetched_With_STJ_Should_Succeed()
    {
        StoreOptions(options =>
        {
            options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

            options.Schema.For<BigIntegerObject>()
                .Duplicate(x => x.Value);
        });

        using (var session = theStore.LightweightSession())
        {
            var obj = new BigIntegerObject { Id = 1, Value = BigInteger.Parse(LargerThanLongValue) };

            session.Store(obj);
            session.SaveChanges();
        }

        StoreOptions(options =>
        {
            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.Schema.For<BigIntegerObject>()
                .Duplicate(x => x.Value);
        }, false);

        using (var session = theStore.QuerySession())
        {
            var result = session.Load<BigIntegerObject>(1);

            result.Value.ToString().ShouldBe(LargerThanLongValue);
        }
    }

    [Fact]
    public void When_Stored_With_STJ_And_Fetched_With_Newtonsoft_Should_Succeed()
    {
        StoreOptions(options =>
        {
            options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

            options.Schema.For<BigIntegerObject>()
                .Duplicate(x => x.Value);
        });

        using (var session = theStore.LightweightSession())
        {
            var obj = new BigIntegerObject { Id = 1, Value = BigInteger.Parse(LargerThanLongValue) };

            session.Store(obj);
            session.SaveChanges();
        }

        StoreOptions(options =>
        {
            options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

            options.Schema.For<BigIntegerObject>()
                .Duplicate(x => x.Value);
        }, false);

        using (var session = theStore.QuerySession())
        {
            var result = session.Load<BigIntegerObject>(1);

            result.Value.ToString().ShouldBe(LargerThanLongValue);
        }
    }
}

public class BigIntegerObject
{
    public int Id { get; set; }

    public BigInteger Value { get; set; }
}
