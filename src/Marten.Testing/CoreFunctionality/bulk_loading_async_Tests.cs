using System.Linq;
using System.Threading.Tasks;
using Weasel.Postgresql;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class bulk_loading_async_Tests : IntegrationContext
    {
        [Fact]
        public async Task load_with_ignore_duplicates()
        {
            var data1 = Target.GenerateRandomData(100).ToArray();

            await theStore.BulkInsertAsync(data1);

            var data2 = Target.GenerateRandomData(50).ToArray();

            // Rigging up data2 so 5 of its values would be getting lost
            for (var i = 0; i < 5; i++)
            {
                data2[i].Id = data1[i].Id;
                data2[i].Number = -1;
            }

            await theStore.BulkInsertAsync(data2, BulkInsertMode.IgnoreDuplicates);

            using var session = theStore.QuerySession();
            session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

            for (var i = 0; i < 5; i++)
            {
                session.Load<Target>(data1[i].Id).Number.ShouldBeGreaterThanOrEqualTo(0);
            }
        }

        [Fact]
        public async Task load_with_multiple_batches()
        {
            var data = Target.GenerateRandomData(100).ToArray();

            await theStore.BulkInsertAsync(data, batchSize: 15);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();
        }

        [Fact]
        public async Task load_with_overwrite_duplicates()
        {
            var data1 = Target.GenerateRandomData(100).ToArray();

            await theStore.BulkInsertAsync(data1);

            var data2 = Target.GenerateRandomData(50).ToArray();

            // Rigging up data2 so 5 of its values would be getting lost
            for (var i = 0; i < 5; i++)
            {
                data2[i].Id = data1[i].Id;
                data2[i].Number = -1;
            }

            await theStore.BulkInsertAsync(data2, BulkInsertMode.OverwriteExisting);

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Count().ShouldBe(data1.Length + data2.Length - 5);

                // Values were overwritten
                for (var i = 0; i < 5; i++)
                {
                    session.Load<Target>(data1[i].Id).Number.ShouldBe(-1);
                }

                var count = session.Connection.CreateCommand()
                    .Sql("select count(*) from mt_doc_target where mt_last_modified is null")
                    .ExecuteScalar();

                count.ShouldBe(0);
            }
        }

        [Fact]
        public async Task load_with_small_batch()
        {
            #region sample_using_bulk_insert_async
            // This is just creating some randomized
            // document data
            var data = Target.GenerateRandomData(100).ToArray();

            // Load all of these into a Marten-ized database
            await theStore.BulkInsertAsync(data, batchSize: 500);

            // And just checking that the data is actually there;)
            theSession.Query<Target>().Count().ShouldBe(data.Length);
            #endregion

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();
        }

        [Fact]
        public async Task load_with_small_batch_and_duplicated_data_field()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Duplicate(x => x.Date);
            });

            var data = Target.GenerateRandomData(100).ToArray();

            await theStore.BulkInsertAsync(data);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            var cmd = theSession.Query<Target>().Where(x => x.Date == data[0].Date).ToCommand();

            theSession.Query<Target>().Any(x => x.Date == data[0].Date)
                .ShouldBeTrue();
        }

        [Fact]
        public async Task load_with_small_batch_and_duplicated_fields()
        {
            StoreOptions(_ => { _.Schema.For<Target>().Duplicate(x => x.String); });

            var data = Target.GenerateRandomData(100).ToArray();

            await theStore.BulkInsertAsync(data);

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Query<Target>().Any(x => x.String == data[0].String)
                .ShouldBeTrue();
        }

        internal async Task BulkInsertModeSamples()
        {
            #region sample_BulkInsertMode_usages

            // Just say we have an array of documents we want to bulk insert
            var data = Target.GenerateRandomData(100).ToArray();

            using var store = DocumentStore.For("some connection string");

            // Discard any documents that match the identity of an existing document
            // in the database
            await store.BulkInsertDocumentsAsync(data, BulkInsertMode.IgnoreDuplicates);

            // This is the default mode, the bulk insert will fail if any duplicate
            // identities with existing data or within the data set being loaded are detected
            await store.BulkInsertDocumentsAsync(data, BulkInsertMode.InsertsOnly);

            // Overwrite any existing documents with the same identity as the documents
            // being loaded
            await store.BulkInsertDocumentsAsync(data, BulkInsertMode.OverwriteExisting);

            #endregion
        }

        internal async Task MultiTenancySample()
        {
            #region sample_MultiTenancyWithBulkInsert

            // Just say we have an array of documents we want to bulk insert
            var data = Target.GenerateRandomData(100).ToArray();

            using var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");
                opts.Policies.AllDocumentsAreMultiTenanted();
            });

            // If multi-tenanted
            await store.BulkInsertDocumentsAsync("a tenant id", data);

            #endregion
        }

        [Fact]
        public async Task load_with_small_batch_and_ignore_duplicates_smoke_test()
        {
            #region sample_bulk_insert_async_with_IgnoreDuplicates
            var data = Target.GenerateRandomData(100).ToArray();

            await theStore.BulkInsertAsync(data, BulkInsertMode.IgnoreDuplicates);
            #endregion

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();

            var count = theSession.Connection.CreateCommand()
                .Sql("select count(*) from mt_doc_target where mt_last_modified is null")
                .ExecuteScalar();

            count.ShouldBe(0);
        }

        [Fact]
        public async Task load_with_small_batch_and_overwrites_smoke_test()
        {
            #region sample_bulk_insert_async_with_OverwriteExisting
            var data = Target.GenerateRandomData(100).ToArray();

            await theStore.BulkInsertAsync(data, BulkInsertMode.OverwriteExisting);
            #endregion

            theSession.Query<Target>().Count().ShouldBe(data.Length);

            theSession.Load<Target>(data[0].Id).ShouldNotBeNull();
        }

        public bulk_loading_async_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
