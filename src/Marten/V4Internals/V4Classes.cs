using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Model;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Storage;
using Marten.Util;
using Marten.V4Internals.Linq;
using Npgsql;
using NpgsqlTypes;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.V4Internals
{
    /*
     * IDEAS
     *
     * 1. Eliminate IQueryableDocument in favor of IDocumentStorage<T>
     * 2. Have a new DocumentStorageGraph to access IDocumentStorage<T>, make
     *    it do the "does exist" checks right there and then
     *    a.) Subclass this storage graph so you don't even do the check
     *        if AutoCreate.None
     *
     */

    public interface IMartenSession : IQueryProvider
    {
        ISerializer Serializer { get; }
        Dictionary<Type, object> ItemMap { get; }
        ITenant Tenant { get; }

        VersionTracker Versions { get; }

        Task<T> ExecuteQuery<T>(IQueryHandler<T> handler, CancellationToken token);
        T ExecuteQuery<T>(IQueryHandler<T> handler);
    }



    public class DocumentStorageGraph: IDocumentStorageGraph
    {
        public IDocumentStorage<T> StorageFor<T>(StorageStyle style)
        {
            throw new NotImplementedException();
        }

        public IBulkLoader<T> BulkLoaderFor<T>()
        {
            throw new NotImplementedException();
        }
    }


    public interface IDocumentStorageGraph
    {
        IDocumentStorage<T> StorageFor<T>(StorageStyle style);
        IBulkLoader<T> BulkLoaderFor<T>();
    }


    // What if we use three different implementations depending on
    // query only, lightweight, identity map, and dirty tracking?

    // Needs to have access to the assignment strategy!!!!
    // Encapsulates the assignment strategy
    public interface IDocumentStorage<T, TId> : IDocumentStorage<T>
    {
        IStorageOperation DeleteForId(TId id);
        IQueryHandler<T> Load(TId id);
        IQueryHandler<T> LoadMany(TId[] ids);
    }


    // Same this time
    public interface IQueryHandler
    {
        void ConfigureCommand(CommandBuilder builder, IMartenSession session);
    }

    public interface ISelector
    {
        void WriteSelectClause(CommandBuilder sql, bool withStatistics);

        IQueryHandler<List<T>> ToListHandler<T>(Statement statement, bool withStatistics);

        IQueryHandler<T> ToSingleHandler<T>(Statement statement, ChoiceResultOperatorBase @operator);

        IQueryHandler<T> ToScalarHandler<T>(Statement statement, ChoiceResultOperatorBase @operator);

        IQueryHandler<int> ToCount(Statement statement);
        IQueryHandler<int> ToLongCount(Statement statement);
        IQueryHandler<bool> ToAny(Statement statement);

        IQueryHandler<T> ToSingle<T>(Statement statement);
        IQueryHandler<T> ToSingleOrDefault<T>(Statement statement);

        IQueryHandler<T> ToFirst<T>(Statement statement);
        IQueryHandler<T> ToFirstOrDefault<T>(Statement statement);
    }



    // Same this time
    public interface IQueryHandler<T> : IQueryHandler
    {
        Type SourceType { get; }

        T Handle(DbDataReader reader, IMartenSession session, QueryStatistics stats);

        Task<T> HandleAsync(DbDataReader reader, IMartenSession session, QueryStatistics stats, CancellationToken token);
    }

    public enum StorageRole
    {
        Upsert,
        Insert,
        Update,
        Deletion,
        Patch,
        Other
    }

    public interface IStorageOperation : IQueryHandler
    {
        Type DocumentType { get; }

        void Postprocess(DbDataReader reader, IList<Exception> exceptions);

        Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);

        StorageRole Role();
    }

    public abstract class StorageOperation<T, TId> : IStorageOperation
    {
        private readonly T _document;
        private readonly TId _id;

        public StorageOperation(T document, TId id, Guid version)
        {
            _document = document;
            _id = id;
            Version = version;
        }

        public Guid Version { get; }

        // TODO -- improve Lamar to make it possible to use protected members
        public abstract string CommandText();

        // TODO -- simple return for method. Like MethodReturnsValue(string name, Variable)
        public abstract NpgsqlDbType DbType();

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters(CommandText());
            parameters[0].NpgsqlDbType = DbType();
            parameters[0].Value = _id;

            parameters[1].NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameters[1].Value = session.Serializer.ToJson(_document);

            ConfigureParameters(parameters, _document);
        }

        // TODO -- need some Lamar improvements here
        public abstract void ConfigureParameters(NpgsqlParameter[] parameters, T document);

        public Type DocumentType => typeof(T);

        public virtual void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing
        }

        public virtual Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            return Task.CompletedTask;
        }
        public abstract StorageRole Role();
    }



}
