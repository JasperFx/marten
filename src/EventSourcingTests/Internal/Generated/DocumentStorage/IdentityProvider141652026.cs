#pragma warning disable
using EventSourcingTests.Bugs;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Arguments;
using Npgsql;
using System;
using System.Collections.Generic;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Generated.DocumentStorage
{
    // START: UpsertIdentityOperation141652026
    public class UpsertIdentityOperation141652026 : Marten.Internal.Operations.StorageOperation<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity _document;
        private readonly System.Guid _id;
        private readonly System.Collections.Generic.Dictionary<System.Guid, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public UpsertIdentityOperation141652026(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, System.Guid id, System.Collections.Generic.Dictionary<System.Guid, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
        }


        public const string COMMAND_TEXT = "select public.mt_upsert_bug_2025_event_inheritance_in_projection_identity(?, ?, ?, ?)";


        public override string CommandText()
        {
            return COMMAND_TEXT;
        }


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Uuid;
        }


        public override void ConfigureParameters(Npgsql.NpgsqlParameter[] parameters, EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session)
        {
            parameters[0].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_document);
            // .Net Class Type
            parameters[1].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;
            parameters[1].Value = _document.GetType().FullName;
            parameters[2].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            parameters[2].Value = document.Id;
            setVersionParameter(parameters[3]);
        }


        public override void Postprocess(System.Data.Common.DbDataReader reader, System.Collections.Generic.IList<System.Exception> exceptions)
        {
            storeVersion();
        }


        public override System.Threading.Tasks.Task PostprocessAsync(System.Data.Common.DbDataReader reader, System.Collections.Generic.IList<System.Exception> exceptions, System.Threading.CancellationToken token)
        {
            storeVersion();
            // Nothing
            return System.Threading.Tasks.Task.CompletedTask;
        }


        public override Marten.Internal.Operations.OperationRole Role()
        {
            return Marten.Internal.Operations.OperationRole.Upsert;
        }

    }

    // END: UpsertIdentityOperation141652026


    // START: InsertIdentityOperation141652026
    public class InsertIdentityOperation141652026 : Marten.Internal.Operations.StorageOperation<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity _document;
        private readonly System.Guid _id;
        private readonly System.Collections.Generic.Dictionary<System.Guid, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public InsertIdentityOperation141652026(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, System.Guid id, System.Collections.Generic.Dictionary<System.Guid, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
        }


        public const string COMMAND_TEXT = "select public.mt_insert_bug_2025_event_inheritance_in_projection_identity(?, ?, ?, ?)";


        public override string CommandText()
        {
            return COMMAND_TEXT;
        }


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Uuid;
        }


        public override void ConfigureParameters(Npgsql.NpgsqlParameter[] parameters, EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session)
        {
            parameters[0].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_document);
            // .Net Class Type
            parameters[1].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;
            parameters[1].Value = _document.GetType().FullName;
            parameters[2].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            parameters[2].Value = document.Id;
            setVersionParameter(parameters[3]);
        }


        public override void Postprocess(System.Data.Common.DbDataReader reader, System.Collections.Generic.IList<System.Exception> exceptions)
        {
            storeVersion();
        }


        public override System.Threading.Tasks.Task PostprocessAsync(System.Data.Common.DbDataReader reader, System.Collections.Generic.IList<System.Exception> exceptions, System.Threading.CancellationToken token)
        {
            storeVersion();
            // Nothing
            return System.Threading.Tasks.Task.CompletedTask;
        }


        public override Marten.Internal.Operations.OperationRole Role()
        {
            return Marten.Internal.Operations.OperationRole.Insert;
        }

    }

    // END: InsertIdentityOperation141652026


    // START: UpdateIdentityOperation141652026
    public class UpdateIdentityOperation141652026 : Marten.Internal.Operations.StorageOperation<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity _document;
        private readonly System.Guid _id;
        private readonly System.Collections.Generic.Dictionary<System.Guid, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public UpdateIdentityOperation141652026(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, System.Guid id, System.Collections.Generic.Dictionary<System.Guid, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
        }


        public const string COMMAND_TEXT = "select public.mt_update_bug_2025_event_inheritance_in_projection_identity(?, ?, ?, ?)";


        public override string CommandText()
        {
            return COMMAND_TEXT;
        }


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Uuid;
        }


        public override void ConfigureParameters(Npgsql.NpgsqlParameter[] parameters, EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session)
        {
            parameters[0].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_document);
            // .Net Class Type
            parameters[1].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;
            parameters[1].Value = _document.GetType().FullName;
            parameters[2].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            parameters[2].Value = document.Id;
            setVersionParameter(parameters[3]);
        }


        public override void Postprocess(System.Data.Common.DbDataReader reader, System.Collections.Generic.IList<System.Exception> exceptions)
        {
            storeVersion();
            postprocessUpdate(reader, exceptions);
        }


        public override async System.Threading.Tasks.Task PostprocessAsync(System.Data.Common.DbDataReader reader, System.Collections.Generic.IList<System.Exception> exceptions, System.Threading.CancellationToken token)
        {
            storeVersion();
            await postprocessUpdateAsync(reader, exceptions, token);
        }


        public override Marten.Internal.Operations.OperationRole Role()
        {
            return Marten.Internal.Operations.OperationRole.Update;
        }

    }

    // END: UpdateIdentityOperation141652026


    // START: QueryOnlyIdentitySelector141652026
    public class QueryOnlyIdentitySelector141652026 : Marten.Internal.CodeGeneration.DocumentSelectorWithOnlySerializer, Marten.Linq.Selectors.ISelector<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public QueryOnlyIdentitySelector141652026(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity Resolve(System.Data.Common.DbDataReader reader)
        {

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = _serializer.FromJson<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 0);
            return document;
        }


        public async System.Threading.Tasks.Task<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = await _serializer.FromJsonAsync<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 0, token).ConfigureAwait(false);
            return document;
        }

    }

    // END: QueryOnlyIdentitySelector141652026


    // START: LightweightIdentitySelector141652026
    public class LightweightIdentitySelector141652026 : Marten.Internal.CodeGeneration.DocumentSelectorWithVersions<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>, Marten.Linq.Selectors.ISelector<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public LightweightIdentitySelector141652026(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = reader.GetFieldValue<System.Guid>(0);

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = _serializer.FromJson<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            return document;
        }


        public async System.Threading.Tasks.Task<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<System.Guid>(0, token);

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = await _serializer.FromJsonAsync<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            return document;
        }

    }

    // END: LightweightIdentitySelector141652026


    // START: IdentityMapIdentitySelector141652026
    public class IdentityMapIdentitySelector141652026 : Marten.Internal.CodeGeneration.DocumentSelectorWithIdentityMap<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>, Marten.Linq.Selectors.ISelector<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public IdentityMapIdentitySelector141652026(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = reader.GetFieldValue<System.Guid>(0);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = _serializer.FromJson<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            return document;
        }


        public async System.Threading.Tasks.Task<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<System.Guid>(0, token);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = await _serializer.FromJsonAsync<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            return document;
        }

    }

    // END: IdentityMapIdentitySelector141652026


    // START: DirtyTrackingIdentitySelector141652026
    public class DirtyTrackingIdentitySelector141652026 : Marten.Internal.CodeGeneration.DocumentSelectorWithDirtyChecking<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>, Marten.Linq.Selectors.ISelector<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public DirtyTrackingIdentitySelector141652026(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = reader.GetFieldValue<System.Guid>(0);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = _serializer.FromJson<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            StoreTracker(_session, document);
            return document;
        }


        public async System.Threading.Tasks.Task<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<System.Guid>(0, token);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document;
            document = await _serializer.FromJsonAsync<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            StoreTracker(_session, document);
            return document;
        }

    }

    // END: DirtyTrackingIdentitySelector141652026


    // START: QueryOnlyIdentityDocumentStorage141652026
    public class QueryOnlyIdentityDocumentStorage141652026 : Marten.Internal.Storage.QueryOnlyDocumentStorage<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public QueryOnlyIdentityDocumentStorage141652026(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override System.Guid AssignIdentity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id == Guid.Empty) _setter(document, Marten.Schema.Identity.CombGuidIdGeneration.NewGuid());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override System.Guid Identity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.QueryOnlyIdentitySelector141652026(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(System.Guid id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Guid[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: QueryOnlyIdentityDocumentStorage141652026


    // START: LightweightIdentityDocumentStorage141652026
    public class LightweightIdentityDocumentStorage141652026 : Marten.Internal.Storage.LightweightDocumentStorage<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public LightweightIdentityDocumentStorage141652026(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override System.Guid AssignIdentity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id == Guid.Empty) _setter(document, Marten.Schema.Identity.CombGuidIdGeneration.NewGuid());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override System.Guid Identity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.LightweightIdentitySelector141652026(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(System.Guid id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Guid[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: LightweightIdentityDocumentStorage141652026


    // START: IdentityMapIdentityDocumentStorage141652026
    public class IdentityMapIdentityDocumentStorage141652026 : Marten.Internal.Storage.IdentityMapDocumentStorage<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public IdentityMapIdentityDocumentStorage141652026(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override System.Guid AssignIdentity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id == Guid.Empty) _setter(document, Marten.Schema.Identity.CombGuidIdGeneration.NewGuid());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override System.Guid Identity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.IdentityMapIdentitySelector141652026(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(System.Guid id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Guid[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: IdentityMapIdentityDocumentStorage141652026


    // START: DirtyTrackingIdentityDocumentStorage141652026
    public class DirtyTrackingIdentityDocumentStorage141652026 : Marten.Internal.Storage.DirtyCheckedDocumentStorage<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public DirtyTrackingIdentityDocumentStorage141652026(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override System.Guid AssignIdentity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id == Guid.Empty) _setter(document, Marten.Schema.Identity.CombGuidIdGeneration.NewGuid());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertIdentityOperation141652026
            (
                document, Identity(document),
                session.Versions.ForType<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>(),
                _document

            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override System.Guid Identity(EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.DirtyTrackingIdentitySelector141652026(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(System.Guid id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Guid[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: DirtyTrackingIdentityDocumentStorage141652026


    // START: IdentityBulkLoader141652026
    public class IdentityBulkLoader141652026 : Marten.Internal.CodeGeneration.BulkLoader<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid>
    {
        private readonly Marten.Internal.Storage.IDocumentStorage<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid> _storage;

        public IdentityBulkLoader141652026(Marten.Internal.Storage.IDocumentStorage<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity, System.Guid> storage) : base(storage)
        {
            _storage = storage;
        }


        public const string MAIN_LOADER_SQL = "COPY public.mt_doc_bug_2025_event_inheritance_in_projection_identity(\"mt_dotnet_type\", \"id\", \"mt_version\", \"data\") FROM STDIN BINARY";

        public const string TEMP_LOADER_SQL = "COPY mt_doc_bug_2025_event_inheritance_in_projection_identity_temp(\"mt_dotnet_type\", \"id\", \"mt_version\", \"data\") FROM STDIN BINARY";

        public const string COPY_NEW_DOCUMENTS_SQL = "insert into public.mt_doc_bug_2025_event_inheritance_in_projection_identity (\"id\", \"data\", \"mt_version\", \"mt_dotnet_type\", mt_last_modified) (select mt_doc_bug_2025_event_inheritance_in_projection_identity_temp.\"id\", mt_doc_bug_2025_event_inheritance_in_projection_identity_temp.\"data\", mt_doc_bug_2025_event_inheritance_in_projection_identity_temp.\"mt_version\", mt_doc_bug_2025_event_inheritance_in_projection_identity_temp.\"mt_dotnet_type\", transaction_timestamp() from mt_doc_bug_2025_event_inheritance_in_projection_identity_temp left join public.mt_doc_bug_2025_event_inheritance_in_projection_identity on mt_doc_bug_2025_event_inheritance_in_projection_identity_temp.id = public.mt_doc_bug_2025_event_inheritance_in_projection_identity.id where public.mt_doc_bug_2025_event_inheritance_in_projection_identity.id is null)";

        public const string OVERWRITE_SQL = "update public.mt_doc_bug_2025_event_inheritance_in_projection_identity target SET data = source.data, mt_version = source.mt_version, mt_dotnet_type = source.mt_dotnet_type, mt_last_modified = transaction_timestamp() FROM mt_doc_bug_2025_event_inheritance_in_projection_identity_temp source WHERE source.id = target.id";

        public const string CREATE_TEMP_TABLE_FOR_COPYING_SQL = "create temporary table mt_doc_bug_2025_event_inheritance_in_projection_identity_temp as select * from public.mt_doc_bug_2025_event_inheritance_in_projection_identity limit 0";


        public override void LoadRow(Npgsql.NpgsqlBinaryImporter writer, EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Storage.Tenant tenant, Marten.ISerializer serializer)
        {
            writer.Write(document.GetType().FullName, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(document.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
            writer.Write(Marten.Schema.Identity.CombGuidIdGeneration.NewGuid(), NpgsqlTypes.NpgsqlDbType.Uuid);
            writer.Write(serializer.ToJson(document), NpgsqlTypes.NpgsqlDbType.Jsonb);
        }


        public override async System.Threading.Tasks.Task LoadRowAsync(Npgsql.NpgsqlBinaryImporter writer, EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity document, Marten.Storage.Tenant tenant, Marten.ISerializer serializer, System.Threading.CancellationToken cancellation)
        {
            await writer.WriteAsync(document.GetType().FullName, NpgsqlTypes.NpgsqlDbType.Varchar, cancellation);
            await writer.WriteAsync(document.Id, NpgsqlTypes.NpgsqlDbType.Uuid, cancellation);
            await writer.WriteAsync(Marten.Schema.Identity.CombGuidIdGeneration.NewGuid(), NpgsqlTypes.NpgsqlDbType.Uuid, cancellation);
            await writer.WriteAsync(serializer.ToJson(document), NpgsqlTypes.NpgsqlDbType.Jsonb, cancellation);
        }


        public override string MainLoaderSql()
        {
            return MAIN_LOADER_SQL;
        }


        public override string TempLoaderSql()
        {
            return TEMP_LOADER_SQL;
        }


        public override string CreateTempTableForCopying()
        {
            return CREATE_TEMP_TABLE_FOR_COPYING_SQL;
        }


        public override string CopyNewDocumentsFromTempTable()
        {
            return COPY_NEW_DOCUMENTS_SQL;
        }


        public override string OverwriteDuplicatesFromTempTable()
        {
            return OVERWRITE_SQL;
        }

    }

    // END: IdentityBulkLoader141652026


    // START: IdentityProvider141652026
    public class IdentityProvider141652026 : Marten.Internal.Storage.DocumentProvider<EventSourcingTests.Bugs.Bug_2025_event_inheritance_in_projection.Identity>
    {
        private readonly Marten.Schema.DocumentMapping _mapping;

        public IdentityProvider141652026(Marten.Schema.DocumentMapping mapping) : base(new IdentityBulkLoader141652026(new QueryOnlyIdentityDocumentStorage141652026(mapping)), new QueryOnlyIdentityDocumentStorage141652026(mapping), new LightweightIdentityDocumentStorage141652026(mapping), new IdentityMapIdentityDocumentStorage141652026(mapping), new DirtyTrackingIdentityDocumentStorage141652026(mapping))
        {
            _mapping = mapping;
        }


    }

    // END: IdentityProvider141652026


}
