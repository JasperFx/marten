using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs;

public class Bug_964_optimistic_concurrency_with_subclass: BugIntegrationContext
{
    public Bug_964_optimistic_concurrency_with_subclass()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<CloudStorage>()
                .AddSubClass<CloudStorageMinio>()
                .GinIndexJsonData()
                .UseOptimisticConcurrency(true)
                .Metadata(m => m.Version.MapTo(x => x.Version));
        });
    }

    [Fact]
    public async Task should_not_throw_a_ConcurrencyException()
    {
        CloudStorageMinio minio1 = new CloudStorageMinio()
        {
            Description = "Test 1",
            Notes = "Some notes",
            Endpoint = "localhost:5000",
            AccessKey = "1234",
            SecretKey = "super-secret",
            UseSSL = false
        };

        using (var session = theStore.LightweightSession())
        {
            session.Insert(minio1);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            session.UpdateExpectedVersion(minio1, minio1.Version);

            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task should_Throw_ConcurrencyException()
    {
        CloudStorageMinio minio1 = new CloudStorageMinio()
        {
            Description = "Test 1",
            Notes = "Some notes",
            Endpoint = "localhost:5000",
            AccessKey = "1234",
            SecretKey = "super-secret",
            UseSSL = false
        };

        using (var session = theStore.LightweightSession())
        {
            session.Insert(minio1);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            var discardedResult = session.Query<CloudStorageMinio>().SingleOrDefault(p => p.Id == minio1.Id);

            // Because I have loaded the record above, all exceptions go away - even when I still expect them

            CloudStorageMinio minio2 = new CloudStorageMinio()
            {
                Id = minio1.Id,
                Version = Guid.NewGuid(),
                Description = "Something else altogether",
                Notes = "Some other notes",
                Endpoint = "localhost:9000",
                AccessKey = "9985",
                SecretKey = "password1234",
                UseSSL = true
            };

            session.UpdateExpectedVersion(minio2, minio2.Version);

            // It throws ConcurrencyException because there is only one
            // exception
            await Should.ThrowAsync<ConcurrencyException>(async () =>
            {
                await session.SaveChangesAsync();
            });
        }
    }
}

public abstract class CloudStorage
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    public string Description { get; set; }
    public string Notes { get; set; }

    public string CloudStorageType
    {
        get { return this.GetType().Name; }
    }
}

public class CloudStorageMinio: CloudStorage
{
    public string Endpoint { get; set; }
    public string AccessKey { get; set; }
    public string SecretKey { get; set; }
    public bool UseSSL { get; set; } = false;
}
