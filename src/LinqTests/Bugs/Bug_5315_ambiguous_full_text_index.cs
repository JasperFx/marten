using System;
using System.Linq;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables.Indexes;
using Xunit;

namespace LinqTests.Bugs;

/// <summary>
/// #5315. Filed as "the query side picks one of several full text indexes arbitrarily", which turned out
/// to be the second of two mechanisms rather than the one the repro actually demonstrated.
///
/// <para>
/// <b>The one that bites.</b> <c>FullTextIndexDefinition</c> derives its name from the TABLE — and the
/// regConfig when non-default — never from the members it indexes. So two <c>FullTextIndex()</c> calls
/// naming different members collide by construction, and <c>AddFullTextIndexIfDoesNotExist</c> returned
/// the first and threw the second away. The second index was never registered, never created and never
/// searched, with no warning. The dedupe is there so that configuring the same index twice stays
/// idempotent, which is worth keeping; silently dropping a DIFFERENT configuration is not.
/// </para>
///
/// <para>
/// <b>The one behind it.</b> Give both indexes explicit names and they really do register — and then
/// <c>FullTextWhereFragment</c> had a genuine choice to make with nothing in the query to make it with,
/// since <c>regConfig</c> is the only selector the search API has. It resolved that with
/// <c>FirstOrDefault</c>, i.e. by declaration order.
/// </para>
///
/// <para>
/// Both are refused now. Ranking (#5298) makes the second load-bearing rather than merely untidy: a
/// <c>ts_rank</c> computed over a different vector than the <c>@@</c> filtered on is silently wrong
/// rather than slow.
/// </para>
/// </summary>
public class Bug_5315_ambiguous_full_text_index: OneOffConfigurationsContext
{
    public class FtsDoc
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
    }

    [Fact]
    public void a_second_index_over_different_members_is_not_silently_discarded()
    {
        var ex = Should.Throw<AmbiguousFullTextIndexException>(() =>
        {
            var store = StoreOptions(opts =>
            {
                opts.AutoCreateSchemaObjects = AutoCreate.None;
                opts.Schema.For<FtsDoc>()
                    .FullTextIndex(x => x.Title)
                    .FullTextIndex(x => x.Body);
            });

            // MartenRegistry defers configuration into _builder.Alter, so resolving the mapping is what
            // runs it. Asserting on the StoreOptions() call alone passes vacuously.
            store.Options.Storage.MappingFor(typeof(FtsDoc));
        });

        // The message has to name what collided and how to get out of it. An exception that only says
        // "ambiguous" moves the silent failure rather than fixing it.
        ex.Message.ShouldContain("FtsDoc");
        ex.Message.ShouldContain("explicit index name");
    }

    /// <summary>
    /// Idempotence is the reason the dedupe exists and has to survive: configuring the same index twice is
    /// ordinary in composed configuration and stays a no-op.
    /// </summary>
    [Fact]
    public void configuring_the_same_index_twice_is_still_idempotent()
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Schema.For<FtsDoc>()
                .FullTextIndex(x => x.Title, x => x.Body)
                .FullTextIndex(x => x.Title, x => x.Body);
        });

        theStore.Options.Storage.MappingFor(typeof(FtsDoc))
            .Indexes.OfType<FullTextIndexDefinition>()
            .Count().ShouldBe(1);
    }

    /// <summary>
    /// With explicit names both indexes register, and only then is the query-side choice reachable. This
    /// is the case the issue described.
    /// </summary>
    [Fact]
    public void two_explicitly_named_indexes_sharing_a_regconfig_are_refused_at_query_time()
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Schema.For<FtsDoc>()
                .FullTextIndex(i => i.Name = "mt_fts_title", x => x.Title)
                .FullTextIndex(i => i.Name = "mt_fts_body", x => x.Body);
        });

        // Precondition: both really did register. Without this the throw below could be coming from the
        // discard path above, and the test would prove nothing about the query side.
        theStore.Options.Storage.MappingFor(typeof(FtsDoc))
            .Indexes.OfType<FullTextIndexDefinition>()
            .Count().ShouldBe(2);

        var ex = Should.Throw<AmbiguousFullTextIndexException>(() =>
            theSession.Query<FtsDoc>().Where(x => x.PlainTextSearch("hi")).ToCommand());

        ex.Message.ShouldContain("english");
        ex.Message.ShouldContain("regConfig");
    }

    /// <summary>
    /// The escape hatch, and why refusing is reasonable rather than a dead end: distinct regConfig values
    /// let the query's own argument select the index.
    /// </summary>
    [Fact]
    public void distinct_reg_configs_disambiguate()
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Schema.For<FtsDoc>()
                .FullTextIndex("english", x => x.Title)
                .FullTextIndex("simple", x => x.Body);
        });

        var english = theSession.Query<FtsDoc>().Where(x => x.PlainTextSearch("hi")).ToCommand().CommandText;
        english.ShouldContain("'Title'");
        english.ShouldNotContain("'Body'");

        var simple = theSession.Query<FtsDoc>().Where(x => x.PlainTextSearch("hi", "simple")).ToCommand().CommandText;
        simple.ShouldContain("'Body'");
        simple.ShouldNotContain("'Title'");
    }

    /// <summary>
    /// One index is the overwhelmingly common case and must be untouched by either check.
    /// </summary>
    [Fact]
    public void a_single_index_still_resolves()
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Schema.For<FtsDoc>().FullTextIndex(x => x.Title, x => x.Body);
        });

        var sql = theSession.Query<FtsDoc>().Where(x => x.PlainTextSearch("hi")).ToCommand().CommandText;

        sql.ShouldContain("'Title'");
        sql.ShouldContain("'Body'");
    }

    /// <summary>
    /// And a document with no full text index at all keeps falling back to the whole document body.
    /// </summary>
    [Fact]
    public void no_index_falls_back_to_the_document_body()
    {
        StoreOptions(opts => opts.AutoCreateSchemaObjects = AutoCreate.None);

        var sql = theSession.Query<FtsDoc>().Where(x => x.PlainTextSearch("hi")).ToCommand().CommandText;

        sql.ShouldContain("to_tsvector('english'::regconfig, d.data)");
    }
}
