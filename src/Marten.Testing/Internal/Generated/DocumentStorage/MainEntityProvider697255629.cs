// <auto-generated/>
#pragma warning disable
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Testing.Bugs;
using Npgsql;
using System;
using System.Collections.Generic;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Generated.DocumentStorage
{
    // START: UpsertMainEntityOperation697255629
    public class UpsertMainEntityOperation697255629 : Marten.Internal.Operations.StorageOperation<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity _document;
        private readonly long _id;
        private readonly System.Collections.Generic.Dictionary<long, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public UpsertMainEntityOperation697255629(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, long id, System.Collections.Generic.Dictionary<long, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
        }


        public const string COMMAND_TEXT = "select public.mt_upsert_bug_717_permutation_of_linq_queries_mainentity(?, ?, ?, ?)";


        public override string CommandText()
        {
            return COMMAND_TEXT;
        }


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Bigint;
        }


        public override void ConfigureParameters(Npgsql.NpgsqlParameter[] parameters, Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session)
        {
            parameters[0].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_document);
            // .Net Class Type
            parameters[1].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;
            parameters[1].Value = _document.GetType().FullName;
            parameters[2].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint;
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

    // END: UpsertMainEntityOperation697255629
    
    
    // START: InsertMainEntityOperation697255629
    public class InsertMainEntityOperation697255629 : Marten.Internal.Operations.StorageOperation<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity _document;
        private readonly long _id;
        private readonly System.Collections.Generic.Dictionary<long, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public InsertMainEntityOperation697255629(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, long id, System.Collections.Generic.Dictionary<long, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
        }


        public const string COMMAND_TEXT = "select public.mt_insert_bug_717_permutation_of_linq_queries_mainentity(?, ?, ?, ?)";


        public override string CommandText()
        {
            return COMMAND_TEXT;
        }


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Bigint;
        }


        public override void ConfigureParameters(Npgsql.NpgsqlParameter[] parameters, Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session)
        {
            parameters[0].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_document);
            // .Net Class Type
            parameters[1].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;
            parameters[1].Value = _document.GetType().FullName;
            parameters[2].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint;
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

    // END: InsertMainEntityOperation697255629
    
    
    // START: UpdateMainEntityOperation697255629
    public class UpdateMainEntityOperation697255629 : Marten.Internal.Operations.StorageOperation<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity _document;
        private readonly long _id;
        private readonly System.Collections.Generic.Dictionary<long, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public UpdateMainEntityOperation697255629(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, long id, System.Collections.Generic.Dictionary<long, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
        }


        public const string COMMAND_TEXT = "select public.mt_update_bug_717_permutation_of_linq_queries_mainentity(?, ?, ?, ?)";


        public override string CommandText()
        {
            return COMMAND_TEXT;
        }


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Bigint;
        }


        public override void ConfigureParameters(Npgsql.NpgsqlParameter[] parameters, Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session)
        {
            parameters[0].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_document);
            // .Net Class Type
            parameters[1].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;
            parameters[1].Value = _document.GetType().FullName;
            parameters[2].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint;
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

    // END: UpdateMainEntityOperation697255629
    
    
    // START: QueryOnlyMainEntitySelector697255629
    public class QueryOnlyMainEntitySelector697255629 : Marten.Internal.CodeGeneration.DocumentSelectorWithOnlySerializer, Marten.Linq.Selectors.ISelector<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public QueryOnlyMainEntitySelector697255629(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity Resolve(System.Data.Common.DbDataReader reader)
        {

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = _serializer.FromJson<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 0);
            return document;
        }


        public async System.Threading.Tasks.Task<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = await _serializer.FromJsonAsync<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 0, token).ConfigureAwait(false);
            return document;
        }

    }

    // END: QueryOnlyMainEntitySelector697255629
    
    
    // START: LightweightMainEntitySelector697255629
    public class LightweightMainEntitySelector697255629 : Marten.Internal.CodeGeneration.DocumentSelectorWithVersions<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>, Marten.Linq.Selectors.ISelector<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public LightweightMainEntitySelector697255629(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = reader.GetFieldValue<long>(0);

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = _serializer.FromJson<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            return document;
        }


        public async System.Threading.Tasks.Task<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<long>(0, token);

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = await _serializer.FromJsonAsync<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            return document;
        }

    }

    // END: LightweightMainEntitySelector697255629
    
    
    // START: IdentityMapMainEntitySelector697255629
    public class IdentityMapMainEntitySelector697255629 : Marten.Internal.CodeGeneration.DocumentSelectorWithIdentityMap<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>, Marten.Linq.Selectors.ISelector<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public IdentityMapMainEntitySelector697255629(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = reader.GetFieldValue<long>(0);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = _serializer.FromJson<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            return document;
        }


        public async System.Threading.Tasks.Task<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<long>(0, token);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = await _serializer.FromJsonAsync<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            return document;
        }

    }

    // END: IdentityMapMainEntitySelector697255629
    
    
    // START: DirtyTrackingMainEntitySelector697255629
    public class DirtyTrackingMainEntitySelector697255629 : Marten.Internal.CodeGeneration.DocumentSelectorWithDirtyChecking<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>, Marten.Linq.Selectors.ISelector<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public DirtyTrackingMainEntitySelector697255629(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = reader.GetFieldValue<long>(0);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = _serializer.FromJson<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            StoreTracker(_session, document);
            return document;
        }


        public async System.Threading.Tasks.Task<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<long>(0, token);
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document;
            document = await _serializer.FromJsonAsync<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            StoreTracker(_session, document);
            return document;
        }

    }

    // END: DirtyTrackingMainEntitySelector697255629
    
    
    // START: QueryOnlyMainEntityDocumentStorage697255629
    public class QueryOnlyMainEntityDocumentStorage697255629 : Marten.Internal.Storage.QueryOnlyDocumentStorage<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public QueryOnlyMainEntityDocumentStorage697255629(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override long AssignIdentity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id <= 0) _setter(document, database.Sequences.SequenceFor(typeof(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity)).NextLong());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override long Identity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.QueryOnlyMainEntitySelector697255629(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(long id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Int64[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: QueryOnlyMainEntityDocumentStorage697255629
    
    
    // START: LightweightMainEntityDocumentStorage697255629
    public class LightweightMainEntityDocumentStorage697255629 : Marten.Internal.Storage.LightweightDocumentStorage<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public LightweightMainEntityDocumentStorage697255629(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override long AssignIdentity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id <= 0) _setter(document, database.Sequences.SequenceFor(typeof(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity)).NextLong());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override long Identity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.LightweightMainEntitySelector697255629(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(long id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Int64[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: LightweightMainEntityDocumentStorage697255629
    
    
    // START: IdentityMapMainEntityDocumentStorage697255629
    public class IdentityMapMainEntityDocumentStorage697255629 : Marten.Internal.Storage.IdentityMapDocumentStorage<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public IdentityMapMainEntityDocumentStorage697255629(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override long AssignIdentity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id <= 0) _setter(document, database.Sequences.SequenceFor(typeof(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity)).NextLong());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override long Identity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.IdentityMapMainEntitySelector697255629(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(long id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Int64[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: IdentityMapMainEntityDocumentStorage697255629
    
    
    // START: DirtyTrackingMainEntityDocumentStorage697255629
    public class DirtyTrackingMainEntityDocumentStorage697255629 : Marten.Internal.Storage.DirtyCheckedDocumentStorage<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public DirtyTrackingMainEntityDocumentStorage697255629(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override long AssignIdentity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            if (document.Id <= 0) _setter(document, database.Sequences.SequenceFor(typeof(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity)).NextLong());
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertMainEntityOperation697255629
            (
                document, Identity(document),
                session.Versions.ForType<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override long Identity(Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.DirtyTrackingMainEntitySelector697255629(session, _document);
        }


        public override Npgsql.NpgsqlCommand BuildLoadCommand(long id, string tenant)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }


        public override Npgsql.NpgsqlCommand BuildLoadManyCommand(System.Int64[] ids, string tenant)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
        }

    }

    // END: DirtyTrackingMainEntityDocumentStorage697255629
    
    
    // START: MainEntityBulkLoader697255629
    public class MainEntityBulkLoader697255629 : Marten.Internal.CodeGeneration.BulkLoader<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long>
    {
        private readonly Marten.Internal.Storage.IDocumentStorage<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long> _storage;

        public MainEntityBulkLoader697255629(Marten.Internal.Storage.IDocumentStorage<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity, long> storage) : base(storage)
        {
            _storage = storage;
        }


        public const string MAIN_LOADER_SQL = "COPY public.mt_doc_bug_717_permutation_of_linq_queries_mainentity(\"mt_dotnet_type\", \"id\", \"mt_version\", \"data\") FROM STDIN BINARY";

        public const string TEMP_LOADER_SQL = "COPY mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp(\"mt_dotnet_type\", \"id\", \"mt_version\", \"data\") FROM STDIN BINARY";

        public const string COPY_NEW_DOCUMENTS_SQL = "insert into public.mt_doc_bug_717_permutation_of_linq_queries_mainentity (\"id\", \"data\", \"mt_version\", \"mt_dotnet_type\", mt_last_modified) (select mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp.\"id\", mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp.\"data\", mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp.\"mt_version\", mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp.\"mt_dotnet_type\", transaction_timestamp() from mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp left join public.mt_doc_bug_717_permutation_of_linq_queries_mainentity on mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp.id = public.mt_doc_bug_717_permutation_of_linq_queries_mainentity.id where public.mt_doc_bug_717_permutation_of_linq_queries_mainentity.id is null)";

        public const string OVERWRITE_SQL = "update public.mt_doc_bug_717_permutation_of_linq_queries_mainentity target SET data = source.data, mt_version = source.mt_version, mt_dotnet_type = source.mt_dotnet_type, mt_last_modified = transaction_timestamp() FROM mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp source WHERE source.id = target.id";

        public const string CREATE_TEMP_TABLE_FOR_COPYING_SQL = "create temporary table mt_doc_bug_717_permutation_of_linq_queries_mainentity_temp as select * from public.mt_doc_bug_717_permutation_of_linq_queries_mainentity limit 0";


        public override void LoadRow(Npgsql.NpgsqlBinaryImporter writer, Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Storage.Tenant tenant, Marten.ISerializer serializer)
        {
            writer.Write(document.GetType().FullName, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(document.Id, NpgsqlTypes.NpgsqlDbType.Bigint);
            writer.Write(Marten.Schema.Identity.CombGuidIdGeneration.NewGuid(), NpgsqlTypes.NpgsqlDbType.Uuid);
            writer.Write(serializer.ToJson(document), NpgsqlTypes.NpgsqlDbType.Jsonb);
        }


        public override async System.Threading.Tasks.Task LoadRowAsync(Npgsql.NpgsqlBinaryImporter writer, Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity document, Marten.Storage.Tenant tenant, Marten.ISerializer serializer, System.Threading.CancellationToken cancellation)
        {
            await writer.WriteAsync(document.GetType().FullName, NpgsqlTypes.NpgsqlDbType.Varchar, cancellation);
            await writer.WriteAsync(document.Id, NpgsqlTypes.NpgsqlDbType.Bigint, cancellation);
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

    // END: MainEntityBulkLoader697255629
    
    
    // START: MainEntityProvider697255629
    public class MainEntityProvider697255629 : Marten.Internal.Storage.DocumentProvider<Marten.Testing.Bugs.Bug_717_permutation_of_Linq_queries.MainEntity>
    {
        private readonly Marten.Schema.DocumentMapping _mapping;

        public MainEntityProvider697255629(Marten.Schema.DocumentMapping mapping) : base(new MainEntityBulkLoader697255629(new QueryOnlyMainEntityDocumentStorage697255629(mapping)), new QueryOnlyMainEntityDocumentStorage697255629(mapping), new LightweightMainEntityDocumentStorage697255629(mapping), new IdentityMapMainEntityDocumentStorage697255629(mapping), new DirtyTrackingMainEntityDocumentStorage697255629(mapping))
        {
            _mapping = mapping;
        }


    }

    // END: MainEntityProvider697255629
    
    
}
