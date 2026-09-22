using System;
using System.Linq;
using JasperFx;
using Marten;
using Marten.Linq;
using Marten.Schema.Indexing.FullText;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Reproduces and locks down the SQL injection vector reported against the
// regConfig parameter on Marten's full-text search APIs. Pre-fix, the regConfig
// string was interpolated directly into the generated SQL by
// FullTextWhereFragment, making every overload below a sink for arbitrary
// PostgreSQL syntax: time-based blind, information disclosure, DDL, etc.
//
// The tests below assert that a regConfig value containing an obvious payload
// (single quote, semicolon, comment marker, sleep call) does NOT survive into
// the generated SQL — either by being rejected at query-construction time
// (preferred) or by being stripped/escaped. Either outcome breaks the
// injection vector.
public class full_text_regconfig_sql_injection
{
    private const string TimeBasedBlindPayload = "english'::text); SELECT pg_sleep(5); --";
    private const string ExfiltrationPayload = "english'; SELECT version(); --";
    private const string DdlPayload = "english'; DROP TABLE mt_doc_article; --";

    public class Article
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }

    private static IDocumentStore BuildStore() => DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.None;
    });

    // The full set of full-text search overloads that take a user-controllable
    // regConfig argument and route through FullTextWhereFragment.
    public static TheoryData<string, Func<IQueryable<Article>, string, IQueryable<Article>>> Overloads => new()
    {
        { nameof(LinqExtensions.Search),          (q, rc) => q.Where(x => x.Search("term", rc)) },
        { nameof(LinqExtensions.PlainTextSearch), (q, rc) => q.Where(x => x.PlainTextSearch("term", rc)) },
        { nameof(LinqExtensions.PhraseSearch),    (q, rc) => q.Where(x => x.PhraseSearch("term", rc)) },
        { nameof(LinqExtensions.WebStyleSearch),  (q, rc) => q.Where(x => x.WebStyleSearch("term", rc)) },
        { nameof(LinqExtensions.PrefixSearch),    (q, rc) => q.Where(x => x.PrefixSearch("term", rc)) },
    };

    private static readonly string[] InjectionPayloads =
    [
        TimeBasedBlindPayload,
        ExfiltrationPayload,
        DdlPayload,
    ];

    [Theory]
    [MemberData(nameof(Overloads))]
    public void rejects_injection_payloads_in_regConfig(
        string overloadName,
        Func<IQueryable<Article>, string, IQueryable<Article>> apply)
    {
        _ = overloadName;
        using var store = BuildStore();
        using var session = store.LightweightSession();

        foreach (var payload in InjectionPayloads)
        {
            var query = apply(session.Query<Article>(), payload);

            // Either the query refuses to materialise (validation throws), or
            // the rendered SQL must NOT contain the raw payload. Both outcomes
            // close the injection vector; we accept either.
            string? sql = null;
            try
            {
                sql = query.ToCommand(FetchType.FetchMany).CommandText;
            }
            catch (ArgumentException)
            {
                // Acceptable: validation rejected the input before SQL was generated.
                continue;
            }

            // None of these tokens should ever appear in SQL generated from a
            // legitimate full-text search; their presence means the payload was
            // interpolated verbatim. The matched substring is what would
            // otherwise execute on the database.
            sql.ShouldNotContain("pg_sleep", Case.Insensitive);
            sql.ShouldNotContain("DROP TABLE", Case.Insensitive);
            sql.ShouldNotContain("SELECT version", Case.Insensitive);
            sql.ShouldNotContain("--");
        }
    }

    [Theory]
    [InlineData("english")]
    [InlineData("french")]
    [InlineData("simple")]
    [InlineData("pg_catalog.english")]
    public void accepts_known_safe_regConfig_values(string regConfig)
    {
        using var store = BuildStore();
        using var session = store.LightweightSession();

        // None of these should throw; all should produce valid SQL referencing
        // the regconfig name (so we know the parameter is still being honored).
        var sql = session.Query<Article>()
            .Where(x => x.PlainTextSearch("term", regConfig))
            .ToCommand(FetchType.FetchMany)
            .CommandText;

        sql.ShouldContain(regConfig);
    }

    // ---------------------------------------------------------------------------------------------
    // GHSA-frqq-p5g3-8jq5.
    //
    // The relevance-ORDERING overloads (#5298, 9.31.0) also take a regConfig, and they were never
    // added to the theory above. They route through TextRankOrdering / FullTextIndexResolver rather
    // than FullTextWhereFragment, where the validator did not exist -- so this file asserted "every
    // overload that takes a user-controllable regConfig" while two of them were unguarded.
    //
    // The fix moves the check into a shared RegConfigValidation and calls it from
    // FullTextIndexResolver.ResolveVector, the sink every search path funnels through. These facts
    // are the part that stops the gap reopening: a new regConfig-taking overload has to be added
    // here, and it inherits the guard automatically.
    // ---------------------------------------------------------------------------------------------

    public static TheoryData<string, Func<IQueryable<Article>, string, IQueryable<Article>>> OrderingOverloads => new()
    {
        {
            nameof(QueryableExtensions.OrderByTextRank),
            (q, rc) => q.Where(x => x.PlainTextSearch("term")).OrderByTextRank("term", TextSearchFunction.Plain, rc)
        },
        {
            nameof(QueryableExtensions.ThenByTextRank),
            (q, rc) => q.Where(x => x.PlainTextSearch("term")).OrderBy(x => x.Title)
                .ThenByTextRank("term", TextSearchFunction.Plain, rc)
        },
    };

    [Theory]
    [MemberData(nameof(OrderingOverloads))]
    public void rejects_injection_payloads_in_ordering_regConfig(
        string overloadName,
        Func<IQueryable<Article>, string, IQueryable<Article>> apply)
    {
        _ = overloadName;
        using var store = BuildStore();
        using var session = store.LightweightSession();

        foreach (var payload in InjectionPayloads)
        {
            string? sql = null;
            try
            {
                sql = apply(session.Query<Article>(), payload).ToCommand(FetchType.FetchMany).CommandText;
            }
            catch (ArgumentException)
            {
                // Preferred outcome: refused before any SQL was generated.
                continue;
            }

            sql.ShouldNotContain("pg_sleep", Case.Insensitive);
            sql.ShouldNotContain("DROP TABLE", Case.Insensitive);
            sql.ShouldNotContain("SELECT version", Case.Insensitive);
            sql.ShouldNotContain("--");
        }
    }

    /// <summary>
    ///     The payload from the report, which breaks out of the literal and appends a subquery to the
    ///     ORDER BY rather than using a semicolon — so it is not stopped by Npgsql's refusal of
    ///     multi-statement command text.
    /// </summary>
    [Fact]
    public void rejects_the_reported_order_by_subquery_payload()
    {
        using var store = BuildStore();
        using var session = store.LightweightSession();

        var payload = "simple'::regconfig, plainto_tsquery('simple','x')) DESC, " +
                      "(SELECT CASE WHEN (SELECT current_setting('is_superuser'))='on' THEN 1 ELSE 0 END) --";

        Should.Throw<ArgumentException>(() => session.Query<Article>()
            .Where(x => x.PlainTextSearch("x"))
            .OrderByTextRank("x", TextSearchFunction.Plain, payload)
            .ToCommand(FetchType.FetchMany));
    }

    [Theory]
    [InlineData("english")]
    [InlineData("simple")]
    [InlineData("pg_catalog.english")]
    public void ordering_accepts_known_safe_regConfig_values(string regConfig)
    {
        using var store = BuildStore();
        using var session = store.LightweightSession();

        var sql = session.Query<Article>()
            .Where(x => x.PlainTextSearch("term", regConfig))
            .OrderByTextRank("term", TextSearchFunction.Plain, regConfig)
            .ToCommand(FetchType.FetchMany)
            .CommandText;

        sql.ShouldContain("ts_rank");
        sql.ShouldContain(regConfig);
    }
}
