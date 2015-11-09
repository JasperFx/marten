using System.Linq;
using Marten.Schema;
using Marten.Testing.Fixtures;
using Shouldly;

namespace Marten.Testing
{
    public class bulk_loading_Tests : DocumentSessionFixture
    {
        public void load_with_small_batch()
        {
            var data = Target.GenerateRandomData(100).ToArray();

            theSession.BulkLoad(data);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();
        }

        public void load_with_small_batch_and_duplicated_fields()
        {
            theContainer.GetInstance<IDocumentSchema>().Alter(_ =>
            {
                _.For<Target>().Searchable(x => x.String);
            });

            var data = Target.GenerateRandomData(100).ToArray();

            theSession.BulkLoad(data);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Query<Target>().Where(x => x.String == data[0].String).Any()
                .ShouldBeTrue();
        }


        public void load_with_multiple_batches()
        {
            var data = Target.GenerateRandomData(100).ToArray();

            theSession.BulkLoad(data, 15);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();
        }

    }
}