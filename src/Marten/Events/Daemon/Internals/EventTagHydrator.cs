#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Tags;
using JasperFx.MultiTenancy;
using Marten.Internal.Sessions;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Daemon.Internals;

/// <summary>
/// marten#6003: reads the stored DCB tags of events the daemon loaded, for a projection that groups by them
/// (<c>IdentityFromTag&lt;TTag&gt;()</c>, which sets <c>EventFilterable.IncludeEventTags</c>). Tags are written at
/// append and never read back by the daemon otherwise, so without this an async tag-grouped projection groups nothing.
/// </summary>
/// <remarks>
/// One extra query per page, by the page's sequences: the <c>tags</c> column in HStore mode, or one probe per
/// registered tag table through its <c>seq_id</c> index. Under conjoined tenancy a row matches its event by sequence
/// and tenant, because under per-tenant event partitioning a sequence is only unique within its tenant.
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Tag types are registered with RegisterTagType, which preserves them per the AOT publishing guide.")]
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Tag types are registered with RegisterTagType, which preserves them per the AOT publishing guide.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Tag types are registered with RegisterTagType, which preserves them per the AOT publishing guide.")]
internal static class EventTagHydrator
{
    private static readonly ConcurrentDictionary<Type, Func<object, object>> _wrappers = new();

    public static async Task HydrateAsync(EventGraph events, QuerySession session, IReadOnlyList<IEvent> loaded,
        CancellationToken token)
    {
        if (loaded.Count == 0 || events.TagTypes.Count == 0) return;

        var conjoined = events.TenancyStyle == TenancyStyle.Conjoined;
        var bySequence = new Dictionary<(long, string?), IEvent>(loaded.Count);
        foreach (var @event in loaded)
        {
            bySequence[(@event.Sequence, conjoined ? @event.TenantId : null)] = @event;
        }

        var sequences = loaded.Select(x => x.Sequence).Distinct().ToArray();
        var schema = events.DatabaseSchemaName;

        if (events.DcbStorageMode == DcbStorageMode.HStore)
        {
            var command = new NpgsqlCommand(
                $"select seq_id, tenant_id, tags from {schema}.mt_events where seq_id = any(:seqs) and tags is not null");
            command.Parameters.AddWithValue("seqs", NpgsqlDbType.Array | NpgsqlDbType.Bigint, sequences);

            await using var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var sequence = reader.GetInt64(0);
                var tenantId = conjoined ? reader.GetString(1) : null;
                if (!bySequence.TryGetValue((sequence, tenantId), out var @event)) continue;

                var tags = await reader.GetFieldValueAsync<Dictionary<string, string?>>(2, token)
                    .ConfigureAwait(false);
                foreach (var (suffix, text) in tags)
                {
                    if (text == null) continue;

                    var registration = events.TagTypes.FirstOrDefault(x => x.TableSuffix == suffix);
                    if (registration == null) continue;

                    addTag(@event, registration, parse(text, registration.SimpleType));
                }
            }

            return;
        }

        foreach (var registration in events.TagTypes)
        {
            var command = new NpgsqlCommand(
                $"select seq_id, {(conjoined ? "tenant_id" : "null")}, value from {schema}.mt_event_tag_{registration.TableSuffix} where seq_id = any(:seqs)");
            command.Parameters.AddWithValue("seqs", NpgsqlDbType.Array | NpgsqlDbType.Bigint, sequences);

            await using var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var sequence = reader.GetInt64(0);
                var tenantId = conjoined ? reader.GetString(1) : null;
                if (!bySequence.TryGetValue((sequence, tenantId), out var @event)) continue;

                addTag(@event, registration, reader.GetValue(2));
            }
        }
    }

    private static void addTag(IEvent @event, ITagTypeRegistration registration, object value)
    {
        var tag = wrap(registration, value);

        if (@event.Tags != null)
        {
            foreach (var existing in @event.Tags)
            {
                if (existing.TagType == registration.TagType && Equals(existing.Value, tag)) return;
            }
        }

        @event.AddTag(new EventTag(registration.TagType, tag));
    }

    // The stored simple value as the registered tag type
    private static object wrap(ITagTypeRegistration registration, object value)
    {
        if (registration.TagType == registration.SimpleType) return value;

        var wrapper = _wrappers.GetOrAdd(registration.TagType, tagType =>
        {
            if (StrongTypedIdResolver.TryResolve(tagType, out var info, StrongTypedIdStyle.AnyType))
            {
                return raw => StrongTypedIdResolver.Wrap(info, raw);
            }

            throw new InvalidOperationException(
                $"Marten can't build a {tagType.FullNameInCode()} tag from its stored value. A tag type read back for IdentityFromTag has to wrap a single Guid, string, int or long value.");
        });

        return wrapper(value);
    }

    private static object parse(string text, Type simpleType)
    {
        if (simpleType == typeof(Guid)) return Guid.Parse(text);
        if (simpleType == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
        if (simpleType == typeof(long)) return long.Parse(text, CultureInfo.InvariantCulture);
        if (simpleType == typeof(short)) return short.Parse(text, CultureInfo.InvariantCulture);
        return text;
    }
}
