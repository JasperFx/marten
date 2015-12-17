using System.Diagnostics;
using System.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class bulk_loading_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void load_with_small_batch()
        {
            // SAMPLE: using_bulk_insert
            // This is just creating some randomized
            // document data
            var data = Target.GenerateRandomData(100).ToArray();

            // Load all of these into a Marten-ized database
            theStore.BulkInsert(data);

            // And just checking that the data is actually there;)
            theSession.Query<Target>().Count().ShouldBe(data.Length);
            // ENDSAMPLE


            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();


            Debug.WriteLine(DocumentStorageBuilder.GenerateDocumentStorageCode(new DocumentMapping[] {new DocumentMapping(typeof(Target)), }));
        }

        [Fact]
        public void load_with_small_batch_and_duplicated_fields()
        {
            theContainer.GetInstance<IDocumentSchema>().Alter(_ =>
            {
                _.For<Target>().Searchable(x => x.String);
            });

            var data = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(data);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Query<Target>().Where(x => x.String == data[0].String).Any()
                .ShouldBeTrue();
        }

        [Fact]
        public void load_with_small_batch_and_duplicated_data_field()
        {
            theContainer.GetInstance<IDocumentSchema>().Alter(_ =>
            {
                _.For<Target>().Searchable(x => x.Date);
            });

            var data = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(data);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Query<Target>().Where(x => x.Date == data[0].Date).Any()
                .ShouldBeTrue();
        }


        [Fact]
        public void load_with_multiple_batches()
        {
            var data = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(data, 15);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();
        }

    }
}