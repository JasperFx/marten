using System.Threading.Tasks;
using Marten.Services.BatchQuerying;
using Marten.Testing.Harness;
using Shouldly;

namespace ValueTypeTests;

public class using_in_batch_queries : OneOffConfigurationsContext
{
    [Fact]
    public async Task load_one_at_a_time()
    {
        var teacher1 = new Teacher();
        var teacher2 = new Teacher();
        var teacher3 = new Teacher();
        var teacher4 = new Teacher();

        theSession.Store(teacher1, teacher2, teacher3, teacher4);
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Teacher>(teacher4.Id);
        loaded.ShouldNotBeNull();

        var batch = theSession.CreateBatchQuery();
        var teacher1_task = batch.Load<Teacher>(teacher1.Id);
        var teacher2_task = batch.Load<Teacher>(teacher2.Id);
        var teacher3_task = batch.Load<Teacher>(teacher3.Id);

        await batch.Execute();

        (await teacher1_task).ShouldNotBeNull();
        (await teacher2_task).ShouldNotBeNull();
        (await teacher3_task).ShouldNotBeNull();
    }
}
