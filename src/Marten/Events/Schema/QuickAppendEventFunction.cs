using System.IO;
using System.Linq;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql.Functions;

namespace Marten.Events.Schema;

    public class QuickAppendEventFunction : Function
    {
        private readonly EventGraph _events;

        public QuickAppendEventFunction(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_quick_append_events"))
        {
            _events = events;
        }

        public override void WriteCreateStatement(Migrator migrator, TextWriter writer)
        {
            var streamIdType = _events.GetStreamIdDBType();
            var databaseSchema = _events.DatabaseSchemaName;

            var tenancyStyle = _events.TenancyStyle;

            var streamsWhere = "id = stream";

            if (tenancyStyle == TenancyStyle.Conjoined)
            {
                streamsWhere += " AND tenant_id = tenantid";
            }

            var table = new EventsTable(_events);
            var metadataColumns = "";
            var metadataParameters = "";
            var metadataValues = "";

            if (table.Columns.OfType<CausationIdColumn>().Any())
            {
                metadataColumns += ", " + CausationIdColumn.ColumnName;
                metadataParameters += ", causation_ids varchar[]";
                metadataValues += ", causation_ids[index]";
            }

            if (table.Columns.OfType<CorrelationIdColumn>().Any())
            {
                metadataColumns += ", " + CorrelationIdColumn.ColumnName;
                metadataParameters += ", correlation_ids varchar[]";
                metadataValues += ", correlation_ids[index]";
            }

            if (table.Columns.OfType<HeadersColumn>().Any())
            {
                metadataColumns += ", " + HeadersColumn.ColumnName;
                metadataParameters += ", headers jsonb[]";
                metadataValues += ", headers[index]";
            }

            if (table.Columns.OfType<UserNameColumn>().Any())
            {
                metadataColumns += ", " + UserNameColumn.ColumnName;
                metadataParameters += ", user_names varchar[]";
                metadataValues += ", user_names[index]";
            }

            var timestampValue = "(now() at time zone 'utc')";
            if (_events.AppendMode == EventAppendMode.QuickWithServerTimestamps)
            {
                timestampValue = "timestamps[index]";
                // 9.0 (#default-flips): use the PG canonical long form so Weasel's
                // string-based schema diff doesn't false-positive on every comparison.
                metadataParameters += ", timestamps timestamp with time zone[]";
            }

            // Add tag type parameters. In DcbStorageMode.HStore the function does NOT
            // accept per-tag-table arrays — tags are written via a follow-up UPDATE
            // (SetEventTagsHstoreByEventIdOperation) by the QuickEventAppender so the
            // function stays mode-agnostic.
            var tagParameters = "";
            var tagInserts = "";
            if (_events.DcbStorageMode != DcbStorageMode.HStore)
            {
                foreach (var tagType in _events.TagTypes)
                {
                    var paramName = $"tag_{tagType.TableSuffix}_values";
                    tagParameters += $", {paramName} varchar[]";

                    if (tenancyStyle == TenancyStyle.Conjoined)
                    {
                        tagInserts += $@"
		IF {paramName}[index] IS NOT NULL THEN
			INSERT INTO {databaseSchema}.mt_event_tag_{tagType.TableSuffix} (value, tenant_id, seq_id) VALUES ({paramName}[index]::{PostgresqlTypeFor(tagType.SimpleType)}, tenantid, seq) ON CONFLICT DO NOTHING;
		END IF;";
                    }
                    else
                    {
                        tagInserts += $@"
		IF {paramName}[index] IS NOT NULL THEN
			INSERT INTO {databaseSchema}.mt_event_tag_{tagType.TableSuffix} (value, seq_id) VALUES ({paramName}[index]::{PostgresqlTypeFor(tagType.SimpleType)}, seq) ON CONFLICT DO NOTHING;
		END IF;";
                    }
                }
            }

            // When EnableBigIntEvents is true, use bigint for version/sequence/return
            // to prevent integer overflow when sequences exceed int32 range (~2.1B)
            var intType = _events.EnableBigIntEvents ? "bigint" : "int";
            var returnType = _events.EnableBigIntEvents ? "bigint[]" : "int[]";

            // #4596 Phase 1 Session 2: per-tenant sequence pick.
            // - When UseTenantPartitionedEvents is OFF (today's behavior): nextval
            //   the single store-global mt_events_sequence.
            // - When ON: look up the partition_suffix for this tenant in
            //   mt_tenant_partitions ONCE at the very top of the function — before
            //   any INSERTs against the partitioned tables — so an unregistered
            //   tenant surfaces a clean MT002 error instead of PG's opaque
            //   "no partition of relation mt_events found for row" (23514) on the
            //   first insert. Then the per-event step is a cheap EXECUTE …
            //   nextval against the pre-built sequence name. Dynamic SQL via
            //   EXECUTE is needed because the sequence name varies per tenant;
            //   format(%I) handles the identifier quoting safely.
            string sequenceDecl;
            string sequenceResolveUpFront;
            string sequencePickPerEvent;
            if (_events.UseTenantPartitionedEvents)
            {
                var tenantsTable = _events.Options.TenantPartitions!.TenantsTableName;
                sequenceDecl = @"
    tenant_seq_suffix varchar;
    tenant_seq_name varchar;";
                sequenceResolveUpFront = $@"
    select partition_suffix into tenant_seq_suffix from {tenantsTable} where partition_value = tenantid;
    if tenant_seq_suffix IS NULL then
        RAISE EXCEPTION 'Tenant ''%'' has no registered partition. Call AddMartenManagedTenantsAsync before appending events.', tenantid USING ERRCODE = 'MT002';
    end if;
    tenant_seq_name := format('%I.%I', '{databaseSchema}', 'mt_events_sequence_' || tenant_seq_suffix);
";
                sequencePickPerEvent = "        execute 'select nextval(''' || tenant_seq_name || ''')' into seq;";
            }
            else
            {
                sequenceDecl = string.Empty;
                sequenceResolveUpFront = string.Empty;
                sequencePickPerEvent = $"        seq := nextval('{databaseSchema}.mt_events_sequence');";
            }

            // #4614 (#4596 Phase 1 Session 4): the bulk function is the path for
            // *every* event append — including the FetchForWriting /
            // AppendOptimistic / AppendExclusive shapes that pass a
            // caller-supplied ExpectedVersionOnServer. Take it as an optional
            // trailing parameter, default NULL (no check) so previously generated
            // call sites still match the signature, and raise MT003 on mismatch
            // so QuickAppendEventsOperationBase.TryTransform can surface it as
            // EventStreamUnexpectedMaxEventIdException — the same exception type
            // the rich path's UpdateStreamVersion already throws.
            //
            // #4765: emitted unconditionally now (was gated on
            // UseTenantPartitionedEvents). The non-partitioned Quick +
            // ExpectedVersion path used to take a per-event UpdateStreamVersion +
            // QuickAppendEventWithVersion route where the OCC check was C#-side
            // (RecordsAffected). On a concurrent loser the per-event INSERT still
            // fired nextval('mt_events_sequence') before the 23505 raised, and
            // nextval is non-transactional — leaving a permanent gap that stalls
            // the async daemon's high-water detector forever (#4749). Routing
            // every Quick append through this function, which checks the version
            // BEFORE any nextval, closes that gap for the non-partitioned store
            // exactly as it already does for the partitioned one.
            //
            // Emit the parameter type + DEFAULT in PostgreSQL's already-canonical
            // form. pg_get_functiondef (what Weasel's function-diff reads back)
            // rewrites a declared `int` parameter to `integer` and renders a bare
            // `DEFAULT NULL` as `DEFAULT NULL::<type>`. Weasel's CanonicizeSql does
            // NOT normalize either, so emitting `int DEFAULT NULL` here would
            // produce a perpetual "Update" delta (non-idempotent schema). Matching
            // pg's rendering up front keeps the function idempotent. This also
            // fixes the same latent non-idempotency the #4614 partitioned function
            // carried (it emitted `{intType} DEFAULT NULL`); existing partitioned
            // DBs converge cleanly because pg had already stored the canonical form.
            var expectedVersionParamType = _events.EnableBigIntEvents ? "bigint" : "integer";
            var expectedVersionParameter =
                $", expected_version {expectedVersionParamType} DEFAULT NULL::{expectedVersionParamType}";
            var expectedVersionCheck = $@"
    if expected_version IS NOT NULL then
        -- COALESCE turns the NULL we get for a brand-new stream into 0, so a
        -- FetchForWriting against a non-existent stream (which sets
        -- ExpectedVersionOnServer = 0) and a StartStream(id, version: 0) both
        -- land on the new-stream branch instead of mis-firing the guard.
        if COALESCE(event_version, 0) != expected_version then
            RAISE EXCEPTION 'Stream version mismatch on ''%'': expected %, actual %', stream, expected_version, COALESCE(event_version, 0) USING ERRCODE = 'MT003';
        end if;
    end if;
";

            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier}(stream {streamIdType}, stream_type varchar, tenantid varchar, event_ids uuid[], event_types varchar[], dotnet_types varchar[], bodies jsonb[], bdatas bytea[]{metadataParameters}{tagParameters}{expectedVersionParameter}) RETURNS {returnType} AS $$
DECLARE
	event_version {intType};
	stream_is_archived boolean;
	event_type varchar;
	event_id uuid;
	body jsonb;
	index int;
	seq {intType};
    actual_tenant varchar;
	return_value {returnType};{sequenceDecl}
BEGIN{sequenceResolveUpFront}
	select version, is_archived into event_version, stream_is_archived from {databaseSchema}.mt_streams where {streamsWhere};{expectedVersionCheck}
	if event_version IS NULL then
		event_version = 0;
		insert into {databaseSchema}.mt_streams (id, type, version, timestamp, tenant_id) values (stream, stream_type, 0, now(), tenantid);
    else
        if stream_is_archived then
            RAISE EXCEPTION 'Attempted to append event to archived stream with Id ''%''.', stream USING ERRCODE = 'MT001';
        end if;
        if tenantid IS NOT NULL then
            select tenant_id into actual_tenant from {databaseSchema}.mt_streams where {streamsWhere};
            if actual_tenant != tenantid then
                RAISE EXCEPTION 'The tenantid does not match the existing stream';
            end if;
        end if;
	end if;

	index := 1;
	return_value := ARRAY[event_version + array_length(event_ids, 1)];

	foreach event_id in ARRAY event_ids
	loop
{sequencePickPerEvent}
		return_value := array_append(return_value, seq);

	    event_version := event_version + 1;
		event_type = event_types[index];
		body = bodies[index];

		-- #4515 / #4578 / Phase 2: bdatas[index] carries the binary payload
		-- for events opted in to binary serialization (NULL otherwise).
		-- bodies[index] is the {{}} JSON placeholder for those events so the
		-- existing data jsonb NOT NULL constraint stays intact.
		insert into {databaseSchema}.mt_events
			(seq_id, id, stream_id, version, data, bdata, type, tenant_id, timestamp, {SchemaConstants.DotNetTypeColumn}, is_archived{metadataColumns})
		values
			(seq, event_id, stream, event_version, body, bdatas[index], event_type, tenantid, {timestampValue}, dotnet_types[index], FALSE{metadataValues});
{tagInserts}
		index := index + 1;
	end loop;

	update {databaseSchema}.mt_streams set version = event_version, timestamp = now() where {streamsWhere};

	return return_value;
END
$$ LANGUAGE plpgsql;
");
        }

        private static string PostgresqlTypeFor(System.Type simpleType)
        {
            if (simpleType == typeof(string)) return "text";
            if (simpleType == typeof(System.Guid)) return "uuid";
            if (simpleType == typeof(int)) return "integer";
            if (simpleType == typeof(long)) return "bigint";
            if (simpleType == typeof(short)) return "smallint";

            return "text";
        }

    }
