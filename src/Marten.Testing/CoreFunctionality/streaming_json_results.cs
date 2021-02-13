using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Testing.Internals;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class streaming_json_results : IntegrationContext
    {
        public streaming_json_results(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        private T deserialize<T>(Stream stream)
        {
            stream.Position = 0;

            return theStore.Serializer.FromJson<T>(stream);
        }

        [Fact]
        public async Task stream_by_id_miss()
        {
            theStore.Advanced.Clean.DeleteDocumentsFor(typeof(IntDoc));

            var stream = new MemoryStream();
            var found = await theSession.Json.StreamById<IntDoc>(1, stream);
            found.ShouldBeFalse();
        }

        [Fact]
        public async Task stream_by_int_id_hit()
        {
            var doc = new IntDoc();
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var stream = new MemoryStream();
            var found = await theSession.Json.StreamById<IntDoc>(doc.Id, stream);
            found.ShouldBeTrue();

            var target = deserialize<IntDoc>(stream);
            target.Id.ShouldBe(doc.Id);
        }

        [Fact]
        public async Task stream_by_long_id_hit()
        {
            var doc = new LongDoc();
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var stream = new MemoryStream();
            var found = await theSession.Json.StreamById<LongDoc>(doc.Id, stream);
            found.ShouldBeTrue();

            var target = deserialize<LongDoc>(stream);
            target.Id.ShouldBe(doc.Id);
        }

        [Fact]
        public async Task stream_by_string_id_hit()
        {
            var doc = new StringDoc{Id = Guid.NewGuid().ToString()};
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var stream = new MemoryStream();
            var found = await theSession.Json.StreamById<StringDoc>(doc.Id, stream);
            found.ShouldBeTrue();

            var target = deserialize<StringDoc>(stream);
            target.Id.ShouldBe(doc.Id);
        }

        [Fact]
        public async Task stream_by_Guid_id_hit()
        {
            var doc = new GuidDoc{};
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var stream = new MemoryStream();
            var found = await theSession.Json.StreamById<GuidDoc>(doc.Id, stream);
            found.ShouldBeTrue();

            var target = deserialize<GuidDoc>(stream);
            target.Id.ShouldBe(doc.Id);
        }

        [Fact]
        public async Task stream_one_with_linq_miss()
        {
            var stream = new MemoryStream();
            var found = await theSession.Query<Target>().Where(x => x.Id == Guid.NewGuid())
                .StreamOne(stream);

            found.ShouldBeFalse();
        }

        [Fact]
        public async Task stream_one_with_linq_hit()
        {
            var targets = Target.GenerateRandomData(10).ToArray();
            await theStore.BulkInsertAsync(targets);

            var stream = new MemoryStream();
            var found = await theSession.Query<Target>().Where(x => x.Id == targets[3].Id)
                .StreamOne(stream);

            found.ShouldBeTrue();

            var target = deserialize<Target>(stream);

            target.Id.ShouldBe(targets[3].Id);
        }

        [Fact]
        public async Task stream_many_with_no_hits()
        {
            var stream = new MemoryStream();
            await theSession.Query<Target>().Where(x => x.Id == Guid.NewGuid())
                .StreamMany(stream);

            var targets = deserialize<Target[]>(stream);
            targets.Any().ShouldBeFalse();
        }

        [Fact]
        public async Task stream_many_with_multiple_hits()
        {
            var targets = Target.GenerateRandomData(10).ToArray();
            await theStore.BulkInsertAsync(targets);

            var stream = new MemoryStream();
            await theSession.Query<Target>().Take(5)
                .StreamMany(stream);

            var results = deserialize<Target[]>(stream);
            results.Length.ShouldBe(5);
        }

        [Fact]
        public async Task stream_many_with_one_hit()
        {
            var targets = Target.GenerateRandomData(10).ToArray();
            await theStore.BulkInsertAsync(targets);

            var stream = new MemoryStream();
            await theSession.Query<Target>().Take(1)
                .StreamMany(stream);

            var results = deserialize<Target[]>(stream);
            results.Length.ShouldBe(1);
        }
    }
}
