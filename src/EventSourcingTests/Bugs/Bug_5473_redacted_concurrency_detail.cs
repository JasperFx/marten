using System;
using JasperFx.Events;
using Marten.Services;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
///     #5473, part 2. On the rich append path a lost optimistic-concurrency race is detected by the
///     database, and <see cref="EventStreamUnexpectedMaxEventIdExceptionTransform" /> reconstructs the
///     stream id and version by regex over the <c>PostgresException.Detail</c>. Npgsql redacts
///     <c>Detail</c> unless the connection string carries <c>Include Error Detail=true</c>, which a
///     production connection string generally should not — so in most deployments the transform falls
///     through to a message it did not build.
///     <para>
///         That fallback used to pass <c>PostgresException.MessageText</c> straight through, handing
///         the user <c>duplicate key value violates unique constraint "pk_mt_events_stream_and_version"</c>:
///         the right exception TYPE carrying a message that names neither the stream, nor the versions,
///         nor the fact that this was a concurrency failure at all.
///     </para>
/// </summary>
public class Bug_5473_redacted_concurrency_detail
{
    private const string RedactionSentinel = "Detail redacted as it may contain sensitive data. " +
        "Specify 'Include Error Detail' in the connection string to include this information.";

    private const string PostgresText = "duplicate key value violates unique constraint";

    private static Exception Transform(string? detail)
    {
        var original = new PostgresException(PostgresText, "ERROR", "ERROR",
            PostgresErrorCodes.UniqueViolation, detail: detail,
            constraintName: "pk_mt_events_stream_and_version", tableName: "mt_events");

        new EventStreamUnexpectedMaxEventIdExceptionTransform()
            .TryTransform(original, out var transformed).ShouldBeTrue();

        return transformed;
    }

    [Fact]
    public void a_present_detail_still_yields_the_id_and_versions()
    {
        var streamId = Guid.NewGuid();
        var ex = Transform($"Key (stream_id, version)=({streamId}, 4) already exists.")
            .ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();

        ex.Id.ShouldBe(streamId);

        // Expected/Actual are not properties on the lifted type; they only reach the message.
        ex.Message.ShouldBe(
            $"Unexpected starting version number for event stream '{streamId}', expected 3 but was 4");
    }

    [Fact]
    public void a_redacted_detail_no_longer_passes_postgres_text_through()
    {
        var ex = Transform(RedactionSentinel).ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();

        ex.Message.ShouldNotContain(PostgresText);
        ex.Message.ShouldStartWith("Optimistic concurrency failure appending to an event stream");
        ex.Message.ShouldContain("redacted by Npgsql");
        ex.Message.ShouldContain("Include Error Detail=true");
        ex.Message.ShouldContain("pk_mt_events_stream_and_version");
    }

    [Fact]
    public void an_absent_detail_says_so_rather_than_blaming_redaction()
    {
        var ex = Transform(null).ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();

        ex.Message.ShouldNotContain(PostgresText);
        ex.Message.ShouldStartWith("Optimistic concurrency failure appending to an event stream");
        ex.Message.ShouldContain("reported no detail");
        ex.Message.ShouldNotContain("Include Error Detail=true");
        ex.Message.ShouldContain("pk_mt_events_stream_and_version");
    }
}
