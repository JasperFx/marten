// <auto-generated/>
#pragma warning disable
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Arguments;
using Npgsql;
using System.Collections.Generic;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Generated.DocumentStorage
{
    // START: UpsertOrderOperation347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class UpsertOrderOperation347010495 : Marten.Internal.Operations.StorageOperation<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly FSharpTypes.Order _document;
        private readonly FSharpTypes.OrderId _id;
        private readonly System.Collections.Generic.Dictionary<FSharpTypes.OrderId, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public UpsertOrderOperation347010495(FSharpTypes.Order document, FSharpTypes.OrderId id, System.Collections.Generic.Dictionary<FSharpTypes.OrderId, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
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


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Uuid;
        }


        public override void ConfigureParameters(Weasel.Postgresql.IGroupedParameterBuilder parameterBuilder, Weasel.Postgresql.ICommandBuilder builder, FSharpTypes.Order document, Marten.Internal.IMartenSession session)
        {
            builder.Append("select strong_typed_fsharp.mt_upsert_fsharptypes_order(");
            var parameter0 = parameterBuilder.AppendParameter(session.Serializer.ToJson(_document));
            parameter0.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            // .Net Class Type
            var parameter1 = parameterBuilder.AppendParameter(_document.GetType().FullName);
            parameter1.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;

            if (document.Id != null)
            {
                var parameter2 = parameterBuilder.AppendParameter(document.Id.Item);
                parameter2.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            }

            else
            {
                var parameter2 = parameterBuilder.AppendParameter<object>(System.DBNull.Value);
            }

            setVersionParameter(parameterBuilder);
            builder.Append(')');
        }

    }

    // END: UpsertOrderOperation347010495
    
    
    // START: InsertOrderOperation347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class InsertOrderOperation347010495 : Marten.Internal.Operations.StorageOperation<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly FSharpTypes.Order _document;
        private readonly FSharpTypes.OrderId _id;
        private readonly System.Collections.Generic.Dictionary<FSharpTypes.OrderId, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public InsertOrderOperation347010495(FSharpTypes.Order document, FSharpTypes.OrderId id, System.Collections.Generic.Dictionary<FSharpTypes.OrderId, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
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


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Uuid;
        }


        public override void ConfigureParameters(Weasel.Postgresql.IGroupedParameterBuilder parameterBuilder, Weasel.Postgresql.ICommandBuilder builder, FSharpTypes.Order document, Marten.Internal.IMartenSession session)
        {
            builder.Append("select strong_typed_fsharp.mt_insert_fsharptypes_order(");
            var parameter0 = parameterBuilder.AppendParameter(session.Serializer.ToJson(_document));
            parameter0.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            // .Net Class Type
            var parameter1 = parameterBuilder.AppendParameter(_document.GetType().FullName);
            parameter1.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;

            if (document.Id != null)
            {
                var parameter2 = parameterBuilder.AppendParameter(document.Id.Item);
                parameter2.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            }

            else
            {
                var parameter2 = parameterBuilder.AppendParameter<object>(System.DBNull.Value);
            }

            setVersionParameter(parameterBuilder);
            builder.Append(')');
        }

    }

    // END: InsertOrderOperation347010495
    
    
    // START: UpdateOrderOperation347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class UpdateOrderOperation347010495 : Marten.Internal.Operations.StorageOperation<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly FSharpTypes.Order _document;
        private readonly FSharpTypes.OrderId _id;
        private readonly System.Collections.Generic.Dictionary<FSharpTypes.OrderId, System.Guid> _versions;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public UpdateOrderOperation347010495(FSharpTypes.Order document, FSharpTypes.OrderId id, System.Collections.Generic.Dictionary<FSharpTypes.OrderId, System.Guid> versions, Marten.Schema.DocumentMapping mapping) : base(document, id, versions, mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _mapping = mapping;
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


        public override NpgsqlTypes.NpgsqlDbType DbType()
        {
            return NpgsqlTypes.NpgsqlDbType.Uuid;
        }


        public override void ConfigureParameters(Weasel.Postgresql.IGroupedParameterBuilder parameterBuilder, Weasel.Postgresql.ICommandBuilder builder, FSharpTypes.Order document, Marten.Internal.IMartenSession session)
        {
            builder.Append("select strong_typed_fsharp.mt_update_fsharptypes_order(");
            var parameter0 = parameterBuilder.AppendParameter(session.Serializer.ToJson(_document));
            parameter0.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            // .Net Class Type
            var parameter1 = parameterBuilder.AppendParameter(_document.GetType().FullName);
            parameter1.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;

            if (document.Id != null)
            {
                var parameter2 = parameterBuilder.AppendParameter(document.Id.Item);
                parameter2.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            }

            else
            {
                var parameter2 = parameterBuilder.AppendParameter<object>(System.DBNull.Value);
            }

            setVersionParameter(parameterBuilder);
            builder.Append(')');
        }

    }

    // END: UpdateOrderOperation347010495
    
    
    // START: QueryOnlyOrderSelector347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class QueryOnlyOrderSelector347010495 : Marten.Internal.CodeGeneration.DocumentSelectorWithOnlySerializer, Marten.Linq.Selectors.ISelector<FSharpTypes.Order>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public QueryOnlyOrderSelector347010495(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public FSharpTypes.Order Resolve(System.Data.Common.DbDataReader reader)
        {

            FSharpTypes.Order document;
            document = _serializer.FromJson<FSharpTypes.Order>(reader, 0);
            return document;
        }


        public async System.Threading.Tasks.Task<FSharpTypes.Order> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {

            FSharpTypes.Order document;
            document = await _serializer.FromJsonAsync<FSharpTypes.Order>(reader, 0, token).ConfigureAwait(false);
            return document;
        }

    }

    // END: QueryOnlyOrderSelector347010495
    
    
    // START: LightweightOrderSelector347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class LightweightOrderSelector347010495 : Marten.Internal.CodeGeneration.DocumentSelectorWithVersions<FSharpTypes.Order, FSharpTypes.OrderId>, Marten.Linq.Selectors.ISelector<FSharpTypes.Order>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public LightweightOrderSelector347010495(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public FSharpTypes.Order Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = FSharpTypes.OrderId.NewId(reader.GetFieldValue<System.Guid>(0));

            FSharpTypes.Order document;
            document = _serializer.FromJson<FSharpTypes.Order>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            return document;
        }


        public async System.Threading.Tasks.Task<FSharpTypes.Order> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = FSharpTypes.OrderId.NewId(await reader.GetFieldValueAsync<System.Guid>(0, token));

            FSharpTypes.Order document;
            document = await _serializer.FromJsonAsync<FSharpTypes.Order>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            return document;
        }

    }

    // END: LightweightOrderSelector347010495
    
    
    // START: IdentityMapOrderSelector347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class IdentityMapOrderSelector347010495 : Marten.Internal.CodeGeneration.DocumentSelectorWithIdentityMap<FSharpTypes.Order, FSharpTypes.OrderId>, Marten.Linq.Selectors.ISelector<FSharpTypes.Order>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public IdentityMapOrderSelector347010495(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public FSharpTypes.Order Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = FSharpTypes.OrderId.NewId(reader.GetFieldValue<System.Guid>(0));
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            FSharpTypes.Order document;
            document = _serializer.FromJson<FSharpTypes.Order>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            return document;
        }


        public async System.Threading.Tasks.Task<FSharpTypes.Order> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = FSharpTypes.OrderId.NewId(await reader.GetFieldValueAsync<System.Guid>(0, token));
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            FSharpTypes.Order document;
            document = await _serializer.FromJsonAsync<FSharpTypes.Order>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            return document;
        }

    }

    // END: IdentityMapOrderSelector347010495
    
    
    // START: DirtyTrackingOrderSelector347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class DirtyTrackingOrderSelector347010495 : Marten.Internal.CodeGeneration.DocumentSelectorWithDirtyChecking<FSharpTypes.Order, FSharpTypes.OrderId>, Marten.Linq.Selectors.ISelector<FSharpTypes.Order>
    {
        private readonly Marten.Internal.IMartenSession _session;
        private readonly Marten.Schema.DocumentMapping _mapping;

        public DirtyTrackingOrderSelector347010495(Marten.Internal.IMartenSession session, Marten.Schema.DocumentMapping mapping) : base(session, mapping)
        {
            _session = session;
            _mapping = mapping;
        }



        public FSharpTypes.Order Resolve(System.Data.Common.DbDataReader reader)
        {
            var id = FSharpTypes.OrderId.NewId(reader.GetFieldValue<System.Guid>(0));
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            FSharpTypes.Order document;
            document = _serializer.FromJson<FSharpTypes.Order>(reader, 1);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            StoreTracker(_session, document);
            return document;
        }


        public async System.Threading.Tasks.Task<FSharpTypes.Order> ResolveAsync(System.Data.Common.DbDataReader reader, System.Threading.CancellationToken token)
        {
            var id = FSharpTypes.OrderId.NewId(await reader.GetFieldValueAsync<System.Guid>(0, token));
            if (_identityMap.TryGetValue(id, out var existing)) return existing;

            FSharpTypes.Order document;
            document = await _serializer.FromJsonAsync<FSharpTypes.Order>(reader, 1, token).ConfigureAwait(false);
            _session.MarkAsDocumentLoaded(id, document);
            _identityMap[id] = document;
            StoreTracker(_session, document);
            return document;
        }

    }

    // END: DirtyTrackingOrderSelector347010495
    
    
    // START: QueryOnlyOrderDocumentStorage347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class QueryOnlyOrderDocumentStorage347010495 : Marten.Internal.Storage.QueryOnlyDocumentStorage<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public QueryOnlyOrderDocumentStorage347010495(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override FSharpTypes.OrderId AssignIdentity(FSharpTypes.Order document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override FSharpTypes.OrderId Identity(FSharpTypes.Order document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.QueryOnlyOrderSelector347010495(session, _document);
        }


        public override object RawIdentityValue(FSharpTypes.OrderId id)
        {
            return id.Item;
        }


        public override Npgsql.NpgsqlParameter BuildManyIdParameter(FSharpTypes.OrderId[] ids)
        {
            return new(){Value = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ids, x => x.Item)), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid};
        }

    }

    // END: QueryOnlyOrderDocumentStorage347010495
    
    
    // START: LightweightOrderDocumentStorage347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class LightweightOrderDocumentStorage347010495 : Marten.Internal.Storage.LightweightDocumentStorage<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public LightweightOrderDocumentStorage347010495(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override FSharpTypes.OrderId AssignIdentity(FSharpTypes.Order document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override FSharpTypes.OrderId Identity(FSharpTypes.Order document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.LightweightOrderSelector347010495(session, _document);
        }


        public override object RawIdentityValue(FSharpTypes.OrderId id)
        {
            return id.Item;
        }


        public override Npgsql.NpgsqlParameter BuildManyIdParameter(FSharpTypes.OrderId[] ids)
        {
            return new(){Value = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ids, x => x.Item)), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid};
        }

    }

    // END: LightweightOrderDocumentStorage347010495
    
    
    // START: IdentityMapOrderDocumentStorage347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class IdentityMapOrderDocumentStorage347010495 : Marten.Internal.Storage.IdentityMapDocumentStorage<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public IdentityMapOrderDocumentStorage347010495(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override FSharpTypes.OrderId AssignIdentity(FSharpTypes.Order document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override FSharpTypes.OrderId Identity(FSharpTypes.Order document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.IdentityMapOrderSelector347010495(session, _document);
        }


        public override object RawIdentityValue(FSharpTypes.OrderId id)
        {
            return id.Item;
        }


        public override Npgsql.NpgsqlParameter BuildManyIdParameter(FSharpTypes.OrderId[] ids)
        {
            return new(){Value = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ids, x => x.Item)), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid};
        }

    }

    // END: IdentityMapOrderDocumentStorage347010495
    
    
    // START: DirtyTrackingOrderDocumentStorage347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class DirtyTrackingOrderDocumentStorage347010495 : Marten.Internal.Storage.DirtyCheckedDocumentStorage<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly Marten.Schema.DocumentMapping _document;

        public DirtyTrackingOrderDocumentStorage347010495(Marten.Schema.DocumentMapping document) : base(document)
        {
            _document = document;
        }



        public override FSharpTypes.OrderId AssignIdentity(FSharpTypes.Order document, string tenantId, Marten.Storage.IMartenDatabase database)
        {
            return document.Id;
        }


        public override Marten.Internal.Operations.IStorageOperation Update(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpdateOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Insert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.InsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Upsert(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {

            return new Marten.Generated.DocumentStorage.UpsertOrderOperation347010495
            (
                document, Identity(document),
                session.Versions.ForType<FSharpTypes.Order, FSharpTypes.OrderId>(),
                _document
                
            );
        }


        public override Marten.Internal.Operations.IStorageOperation Overwrite(FSharpTypes.Order document, Marten.Internal.IMartenSession session, string tenant)
        {
            throw new System.NotSupportedException();
        }


        public override FSharpTypes.OrderId Identity(FSharpTypes.Order document)
        {
            return document.Id;
        }


        public override Marten.Linq.Selectors.ISelector BuildSelector(Marten.Internal.IMartenSession session)
        {
            return new Marten.Generated.DocumentStorage.DirtyTrackingOrderSelector347010495(session, _document);
        }


        public override object RawIdentityValue(FSharpTypes.OrderId id)
        {
            return id.Item;
        }


        public override Npgsql.NpgsqlParameter BuildManyIdParameter(FSharpTypes.OrderId[] ids)
        {
            return new(){Value = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ids, x => x.Item)), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid};
        }

    }

    // END: DirtyTrackingOrderDocumentStorage347010495
    
    
    // START: OrderBulkLoader347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class OrderBulkLoader347010495 : Marten.Internal.CodeGeneration.BulkLoader<FSharpTypes.Order, FSharpTypes.OrderId>
    {
        private readonly Marten.Internal.Storage.IDocumentStorage<FSharpTypes.Order, FSharpTypes.OrderId> _storage;

        public OrderBulkLoader347010495(Marten.Internal.Storage.IDocumentStorage<FSharpTypes.Order, FSharpTypes.OrderId> storage) : base(storage)
        {
            _storage = storage;
        }


        public const string MAIN_LOADER_SQL = "COPY strong_typed_fsharp.mt_doc_fsharptypes_order(\"mt_dotnet_type\", \"id\", \"mt_version\", \"data\") FROM STDIN BINARY";

        public const string TEMP_LOADER_SQL = "COPY mt_doc_fsharptypes_order_temp(\"mt_dotnet_type\", \"id\", \"mt_version\", \"data\") FROM STDIN BINARY";

        public const string COPY_NEW_DOCUMENTS_SQL = "insert into strong_typed_fsharp.mt_doc_fsharptypes_order (\"id\", \"data\", \"mt_version\", \"mt_dotnet_type\", mt_last_modified) (select mt_doc_fsharptypes_order_temp.\"id\", mt_doc_fsharptypes_order_temp.\"data\", mt_doc_fsharptypes_order_temp.\"mt_version\", mt_doc_fsharptypes_order_temp.\"mt_dotnet_type\", transaction_timestamp() from mt_doc_fsharptypes_order_temp left join strong_typed_fsharp.mt_doc_fsharptypes_order on mt_doc_fsharptypes_order_temp.id = strong_typed_fsharp.mt_doc_fsharptypes_order.id where strong_typed_fsharp.mt_doc_fsharptypes_order.id is null)";

        public const string OVERWRITE_SQL = "update strong_typed_fsharp.mt_doc_fsharptypes_order target SET data = source.data, mt_version = source.mt_version, mt_dotnet_type = source.mt_dotnet_type, mt_last_modified = transaction_timestamp() FROM mt_doc_fsharptypes_order_temp source WHERE source.id = target.id";

        public const string CREATE_TEMP_TABLE_FOR_COPYING_SQL = "create temporary table mt_doc_fsharptypes_order_temp (like strong_typed_fsharp.mt_doc_fsharptypes_order including defaults)";


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


        public override async System.Threading.Tasks.Task LoadRowAsync(Npgsql.NpgsqlBinaryImporter writer, FSharpTypes.Order document, Marten.Storage.Tenant tenant, Marten.ISerializer serializer, System.Threading.CancellationToken cancellation)
        {
            await writer.WriteAsync(document.GetType().FullName, NpgsqlTypes.NpgsqlDbType.Varchar, cancellation);
            await writer.WriteAsync(document.Id.Item, NpgsqlTypes.NpgsqlDbType.Uuid, cancellation);
            await writer.WriteAsync(JasperFx.Core.CombGuidIdGeneration.NewGuid(), NpgsqlTypes.NpgsqlDbType.Uuid, cancellation);
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

    }

    // END: OrderBulkLoader347010495
    
    
    // START: OrderProvider347010495
    [global::System.CodeDom.Compiler.GeneratedCode("JasperFx", "1.0.0")]
    public sealed class OrderProvider347010495 : Marten.Internal.Storage.DocumentProvider<FSharpTypes.Order>
    {
        private readonly Marten.Schema.DocumentMapping _mapping;

        public OrderProvider347010495(Marten.Schema.DocumentMapping mapping) : base(new OrderBulkLoader347010495(new QueryOnlyOrderDocumentStorage347010495(mapping)), new QueryOnlyOrderDocumentStorage347010495(mapping), new LightweightOrderDocumentStorage347010495(mapping), new IdentityMapOrderDocumentStorage347010495(mapping), new DirtyTrackingOrderDocumentStorage347010495(mapping))
        {
            _mapping = mapping;
        }


    }

    // END: OrderProvider347010495
    
    
}

