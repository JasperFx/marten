using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading;

public class check_document_exists: IntegrationContext
{
    public check_document_exists(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task check_exists_by_guid_id_hit()
    {
        var doc = new GuidDoc { Id = Guid.NewGuid() };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<GuidDoc>(doc.Id);
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_by_guid_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<GuidDoc>(Guid.NewGuid());
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_by_int_id_hit()
    {
        var doc = new IntDoc { Id = 42 };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<IntDoc>(42);
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_by_int_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<IntDoc>(999999);
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_by_long_id_hit()
    {
        var doc = new LongDoc { Id = 200L };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<LongDoc>(200L);
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_by_long_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<LongDoc>(999999L);
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_by_string_id_hit()
    {
        var doc = new StringDoc { Id = "test-doc" };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var exists = await theSession.CheckExistsAsync<StringDoc>("test-doc");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task check_exists_by_string_id_miss()
    {
        var exists = await theSession.CheckExistsAsync<StringDoc>("nonexistent");
        exists.ShouldBeFalse();
    }

    #region sample_check_exists_usage

    [Fact]
    public async Task check_exists_by_object_id()
    {
        var doc = new GuidDoc { Id = Guid.NewGuid() };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        // Use the object overload for dynamic id types
        var exists = await theSession.CheckExistsAsync<GuidDoc>((object)doc.Id);
        exists.ShouldBeTrue();
    }

    #endregion
}

public class check_document_exists_in_batch: IntegrationContext
{
    public check_document_exists_in_batch(DefaultStoreFixture fixture): base(fixture)
    {
    }

    #region sample_check_exists_batch_usage

    [Fact]
    public async Task check_exists_in_batch_by_guid_id()
    {
        var doc = new GuidDoc { Id = Guid.NewGuid() };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<GuidDoc>(doc.Id);
        var existsMiss = batch.CheckExists<GuidDoc>(Guid.NewGuid());
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }

    #endregion

    [Fact]
    public async Task check_exists_in_batch_by_int_id()
    {
        var doc = new IntDoc { Id = 77 };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<IntDoc>(77);
        var existsMiss = batch.CheckExists<IntDoc>(888888);
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_in_batch_by_long_id()
    {
        var doc = new LongDoc { Id = 300L };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<LongDoc>(300L);
        var existsMiss = batch.CheckExists<LongDoc>(999999L);
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_in_batch_by_string_id()
    {
        var doc = new StringDoc { Id = "batch-test" };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<StringDoc>("batch-test");
        var existsMiss = batch.CheckExists<StringDoc>("nope");
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }

    [Fact]
    public async Task check_exists_in_batch_by_object_id()
    {
        var doc = new GuidDoc { Id = Guid.NewGuid() };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var existsHit = batch.CheckExists<GuidDoc>((object)doc.Id);
        var existsMiss = batch.CheckExists<GuidDoc>((object)Guid.NewGuid());
        await batch.Execute();

        (await existsHit).ShouldBeTrue();
        (await existsMiss).ShouldBeFalse();
    }
}
