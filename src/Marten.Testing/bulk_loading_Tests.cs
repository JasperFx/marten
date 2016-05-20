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

        }

        [Fact]
        public void load_with_small_batch_and_duplicated_fields()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Searchable(x => x.String);
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
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Searchable(x => x.Date);
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

            theStore.BulkInsert(data, batchSize:15);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();
        }

        [Fact]
        public void load_with_small_batch_and_ignore_duplicates_smoke_test()
        {
            var data = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(data, mode: BulkInsertMode.IgnoreDuplicates);

            theSession.Query<Target>().Count().ShouldBe(data.Length);


            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();

        }

        [Fact]
        public void load_with_small_batch_and_overwrites_smoke_test()
        {
            var data = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(data, mode: BulkInsertMode.OverwriteExisting);

            theSession.Query<Target>().Count().ShouldBe(data.Length);


            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();

        }


        [Fact]
        public void load_with_ignore_duplicates()
        {
            var data1 = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(data1);

            var data2 = Target.GenerateRandomData(50).ToArray();

            // Rigging up data2 so 5 of its values would be getting lost
            for (int i = 0; i < 5; i++)
            {
                data2[i].Id = data1[i].Id;
                data2[i].Number = -1;
            }

            theStore.BulkInsert(data2, mode: BulkInsertMode.IgnoreDuplicates);

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

                for (int i = 0; i < 5; i++)
                {
                    session.Load<Target>(data1[i].Id).Number.ShouldBeGreaterThanOrEqualTo(0);
                }
            }


        }

        [Fact]
        public void load_with_overwrite_duplicates()
        {
            var data1 = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(data1);

            var data2 = Target.GenerateRandomData(50).ToArray();

            // Rigging up data2 so 5 of its values would be getting lost
            for (int i = 0; i < 5; i++)
            {
                data2[i].Id = data1[i].Id;
                data2[i].Number = -1;
            }

            theStore.BulkInsert(data2, mode: BulkInsertMode.OverwriteExisting);

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

                // Values were overwritten
                for (int i = 0; i < 5; i++)
                {
                    session.Load<Target>(data1[i].Id).Number.ShouldBe(-1);
                }
            }
        }


    }
}