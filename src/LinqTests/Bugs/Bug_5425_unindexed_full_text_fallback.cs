using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

/// <summary>
/// #5425. A full text search whose <c>regConfig</c> matches no index on the document falls back to
/// <c>to_tsvector(cfg, d.data)</c> over the whole JSON document. Nothing logged it, warned about it or
/// threw.
///
/// <para>
/// <b>The results stay correct, which is exactly why it is worth a signal.</b> What changes is the query
/// plan — no index can serve that expression, so it is a sequential scan that re-parses every document's
/// JSON on every query — and the matching SCOPE, because every string in the document becomes matchable
/// rather than the indexed members. It works in development and degrades with table size.
/// </para>
///
/// <para>
/// ⚠️ <b>A warning rather than an exception, deliberately.</b> Searches taking this path work today;
/// failing them would break running applications over a performance defect on a minor version. Compare
/// <c>FullTextIndexResolver.FindIndex</c>, which does throw for #5315 — that case is <em>ambiguous</em>
/// rather than merely slow, and has no correct answer to fall back to. The issue offered a hybrid-path
/// throw as its "at minimum"; that was declined for the same reason.
/// </para>
///
/// <para>
/// ⚠️ <b>Scoped to "the type HAS indexes and none matched".</b> A type with no full text index at all is
/// <c>Search()</c> over the whole document — a documented, intended Marten feature — and warning about it
/// would fire on correct code. <c>no_warning_when_the_type_has_no_index_at_all</c> is what holds that line.
/// </para>
/// </summary>
public class Bug_5425_unindexed_full_text_fallback: OneOffConfigurationsContext
{
    public class Memo
    {
        public Guid Id { get; set; }
        public string Body { get; set; }
    }

    public class Unindexed
    {
        public Guid Id { get; set; }
        public string Body { get; set; }
    }

    /// <summary>Captures what Marten logged, so a fact can assert on the absence of a warning too.</summary>
    private sealed class RecordingLogger: ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope: IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private RecordingLogger _logger = null!;

    private async Task<IDocumentStore> aStoreAsync(Action<StoreOptions> configure)
    {
        _logger = new RecordingLogger();

        StoreOptions(opts =>
        {
            configure(opts);
            opts.DotNetLogger = _logger;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        return theStore;
    }

    /// <summary>
    ///     The failure scenario from the issue: the only index is <c>simple</c> and the search defaults to
    ///     <c>english</c>.
    /// </summary>
    [Fact]
    public async Task warns_when_an_index_exists_but_none_matches_the_regconfig()
    {
        var store = await aStoreAsync(opts =>
            opts.Schema.For<Memo>().FullTextIndex("simple", x => x.Body));

        await using var session = store.QuerySession();
        await session.Query<Memo>().Where(x => x.Body.Search("fox")).ToListAsync(
            TestContext.Current.CancellationToken);

        var warning = _logger.Warnings.ShouldHaveSingleItem();

        // The message has to name BOTH halves, or the reader cannot tell what to change: the config it
        // looked for, and the ones actually indexed.
        warning.ShouldContain("english");
        warning.ShouldContain("simple");
        warning.ShouldContain(nameof(Memo));
    }

    /// <summary>
    ///     A matching regConfig produces no signal at all.
    /// </summary>
    [Fact]
    public async Task no_warning_when_the_regconfig_matches_an_index()
    {
        var store = await aStoreAsync(opts =>
            opts.Schema.For<Memo>().FullTextIndex("simple", x => x.Body));

        await using var session = store.QuerySession();
        await session.Query<Memo>()
            .Where(x => x.Body.Search("fox", "simple"))
            .ToListAsync(TestContext.Current.CancellationToken);

        _logger.Warnings.ShouldBeEmpty();
    }

    /// <summary>
    ///     ⚠️ The line that keeps this from firing on correct code. A type with no full text index is
    ///     <c>Search()</c> over the whole document, which Marten documents and supports — it is not the
    ///     mistake #5425 is about, and warning would make the signal worthless by crying wolf on every
    ///     legitimate use.
    /// </summary>
    [Fact]
    public async Task no_warning_when_the_type_has_no_index_at_all()
    {
        var store = await aStoreAsync(opts => opts.RegisterDocumentType<Unindexed>());

        await using var session = store.QuerySession();
        await session.Query<Unindexed>().Where(x => x.Body.Search("fox")).ToListAsync(
            TestContext.Current.CancellationToken);

        _logger.Warnings.ShouldBeEmpty();
    }

    /// <summary>
    ///     Once per document type and regConfig, not once per query — a warning on every search would be
    ///     noise in exactly the high-volume case the defect hurts most.
    /// </summary>
    [Fact]
    public async Task warns_once_per_document_type_and_regconfig()
    {
        var store = await aStoreAsync(opts =>
            opts.Schema.For<Memo>().FullTextIndex("simple", x => x.Body));

        await using var session = store.QuerySession();
        for (var i = 0; i < 3; i++)
        {
            await session.Query<Memo>().Where(x => x.Body.Search("fox")).ToListAsync(
                TestContext.Current.CancellationToken);
        }

        _logger.Warnings.Count.ShouldBe(1);
    }
}
