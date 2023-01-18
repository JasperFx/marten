using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.NodaTimeExtensions;
using Marten.Testing.Harness;
using Newtonsoft.Json.Linq;
using NodaTime;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2307_JObject_in_dictionary_duplicated_field: BugIntegrationContext
{
    [Fact]
    public async Task reproduction()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<TestReadModel>()
                .Identity(x => x.InstanceId);
            opts.UseNodaTime();
        });

        var e = new
        {
            Id = Guid.NewGuid(),
        };

        var test1 = new TestReadModel
        {
            InstanceId = e.Id,
            Status = "progress",
            InstanceData = JObject.FromObject(new { Data = "Pew Pew", }),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
        };


        await using (var session = theStore.LightweightSession())
        {
            session.Store(test1);
            await session.SaveChangesAsync(default);
        }

        await using (var session = theStore.QuerySession())
        {
            var instanceFilesTask = await session.Query<TestReadModel>()
                .Where(x => x.InstanceId == e.Id)
                .Select(x => new { x.InstanceData, x.Status, x.InstanceId, })
                .ToListAsync(default);

            instanceFilesTask.Count.ShouldBePositive();
            instanceFilesTask.First().InstanceData["Data"].ShouldBe("Pew Pew");
        }
    }
}

public class TestReadModel
{
    public Guid InstanceId { get; set; }
    public string Status { get; set; }
    public Instant CreatedAt { get; set; }
    public JObject InstanceData { get; set; }
}
