using System;
using Marten.Testing.Harness;
using Marten.Testing.Weird;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_1647_cannot_use_Task_as_document_name : IntegrationContext
    {
        public Bug_1647_cannot_use_Task_as_document_name(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async System.Threading.Tasks.Task save_and_load_okay()
        {
            var task = new Marten.Testing.Weird.Task {Description = "this is important"};
            theSession.Store(task);

            await theSession.SaveChangesAsync();

            var task2 = await theSession.LoadAsync<Marten.Testing.Weird.Task>(task.Id);
            task2.ShouldNotBeNull();
        }
    }


}


namespace Marten.Testing.Weird
{
    public class Task
    {
        public Guid Id { get; set; }
        public string Description { get; set; }
    }
}
