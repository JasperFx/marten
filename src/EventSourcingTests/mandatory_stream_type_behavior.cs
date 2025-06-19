using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
            theSession.Events.StartStream(Guid.NewGuid(), [new AEvent()]);
        });

        Should.Throw<StreamTypeMissingException>(() =>
        {
            theSession.Events.StartStream([new AEvent()]);
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
            theSession.Events.StartStream(Guid.NewGuid().ToString(), [new AEvent()]);
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

    [Theory]
    [InlineData(EventAppendMode.Rich, StreamIdentity.AsGuid)]
    [InlineData(EventAppendMode.Rich, StreamIdentity.AsString)]
    [InlineData(EventAppendMode.Quick, StreamIdentity.AsGuid)]
    [InlineData(EventAppendMode.Quick, StreamIdentity.AsString)]
    public async Task happy_path_append_event_after_the_stream_exists(EventAppendMode mode, StreamIdentity identity)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Events.StreamIdentity = identity;
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
        });

        if (identity == StreamIdentity.AsGuid)
        {
            var streamId = Guid.NewGuid();
            theSession.Events.StartStream<SimpleAggregate>(streamId, new BEvent());
            await theSession.SaveChangesAsync();
            theSession.Events.Append(streamId, new AEvent());
            await theSession.SaveChangesAsync();
        }
        else
        {
            var streamId = Guid.NewGuid().ToString();
            theSession.Events.StartStream<SimpleAggregate>(streamId, new BEvent());
            await theSession.SaveChangesAsync();
            theSession.Events.Append(streamId, new AEvent());
            await theSession.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task happy_path_usage_with_guid()
    {
        StoreOptions(opts => opts.Events.UseMandatoryStreamTypeDeclaration = true);

        theSession.Events.StartStream<MyAggregate>(Guid.NewGuid(), new AEvent());

        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task happy_path_usage_with_string()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        theSession.Events.StartStream<MyAggregate>(Guid.NewGuid().ToString(), new AEvent());

        await theSession.SaveChangesAsync();
    }

    public static void configure_mandatory_stream_type()
    {
        #region sample_UseMandatoryStreamTypeDeclaration

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // Force users to supply a stream type on StartStream, and disallow
            // appending events if the stream does not already exist
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
        });

        #endregion
    }
}
