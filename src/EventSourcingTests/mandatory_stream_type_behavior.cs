using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class mandatory_stream_type_behavior : OneOffConfigurationsContext
{
    [Fact]
    public void reject_new_stream_if_stream_type_is_omitted_for_guid_identity()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
        });

        Should.Throw<StreamTypeMissingException>(() =>
        {
            theSession.Events.StartStream(Guid.NewGuid(), new object[] { new AEvent() });
        });

        Should.Throw<StreamTypeMissingException>(() =>
        {
            theSession.Events.StartStream(new object[] { new AEvent() });
        });

        Should.Throw<StreamTypeMissingException>(() =>
        {
            theSession.Events.StartStream(new AEvent());
        });
    }

    [Fact]
    public void reject_new_stream_if_stream_type_is_omitted_for_string_identity()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        Should.Throw<StreamTypeMissingException>(() =>
        {
            theSession.Events.StartStream(Guid.NewGuid().ToString(), new object[] { new AEvent() });
        });

        Should.Throw<StreamTypeMissingException>(() =>
        {
            theSession.Events.StartStream(Guid.NewGuid().ToString(), new AEvent());
        });
    }

    [Theory]
    [InlineData(EventAppendMode.Rich, StreamIdentity.AsGuid)]
    [InlineData(EventAppendMode.Rich, StreamIdentity.AsString)]
    [InlineData(EventAppendMode.Quick, StreamIdentity.AsGuid)]
    [InlineData(EventAppendMode.Quick, StreamIdentity.AsString)]
    public async Task throw_on_append_with_no_existing_stream(EventAppendMode mode, StreamIdentity identity)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Events.StreamIdentity = identity;
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
        });

        if (identity == StreamIdentity.AsGuid)
        {
            theSession.Events.Append(Guid.NewGuid(), new AEvent());
        }
        else
        {
            theSession.Events.Append(Guid.NewGuid().ToString(), new AEvent());
        }

        await Should.ThrowAsync<NonExistentStreamException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });
    }
}
