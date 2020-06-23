using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;
using Marten.Services.BatchQuerying;
using Marten.Util;
using Marten.V4Internals.Linq;
using Npgsql;

namespace Marten.V4Internals.Sessions
{
    public class QuerySession : MartenSessionBase, IQuerySession, IQueryProvider
    {
        public QuerySession(IDocumentStore store, IDatabase database, ISerializer serializer, ITenant tenant,
            IPersistenceGraph persistence, StoreOptions options) : base(database, serializer, tenant, persistence, options)
        {
            DocumentStore = store;

        }

        protected override IDocumentStorage<T> selectStorage<T>(DocumentPersistence<T> persistence)
        {
            return persistence.QueryOnly;
        }


        public T Load<T>(string id)
        {
            assertNotDisposed();
            return storageFor<T, string>().Load(id, this);
        }

        public Task<T> LoadAsync<T>(string id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadAsync(id, this, token);
        }

        public T Load<T>(int id)
        {
            assertNotDisposed();
            return storageFor<T, int>().Load(id, this);
        }

        public Task<T> LoadAsync<T>(int id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            return storageFor<T, int>().LoadAsync(id, this, token);
        }

        public T Load<T>(long id)
        {
            assertNotDisposed();
            return storageFor<T, long>().Load(id, this);
        }

        public Task<T> LoadAsync<T>(long id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadAsync(id, this, token);
        }

        public T Load<T>(Guid id)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().Load(id, this);
        }

        public Task<T> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadAsync(id, this, token);
        }


        public IMartenQueryable<T> Query<T>()
        {
            return new V4Queryable<T>(this);
        }

        public IReadOnlyList<T> Query<T>(string sql, params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token = default(CancellationToken), params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public IBatchedQuery CreateBatchQuery()
        {
            throw new NotImplementedException();
        }

        public NpgsqlConnection Connection => Database.Connection;
        public IMartenSessionLogger Logger { get; set; }
        public int RequestCount => Database.RequestCount;
        public IDocumentStore DocumentStore { get; }

        public TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            throw new NotImplementedException();
        }

        public Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<T> LoadMany<T>(params string[] ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadMany(ids, this);
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<string> ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadMany(ids.ToArray(), this);

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params string[] ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids, this, default(CancellationToken));

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<string> ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids.ToArray(), this, default(CancellationToken));
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids, this, token);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<string> ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids.ToArray(), this, token);
        }



        public IReadOnlyList<T> LoadMany<T>(params int[] ids)
        {
            assertNotDisposed();
            return storageFor<T, int>().LoadMany(ids, this);
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<int> ids)
        {
            assertNotDisposed();
            return storageFor<T, int>().LoadMany(ids.ToArray(), this);

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params int[] ids)
        {
            assertNotDisposed();
            return storageFor<T, int>().LoadManyAsync(ids, this, default(CancellationToken));

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<int> ids)
        {
            assertNotDisposed();
            return storageFor<T, int>().LoadManyAsync(ids.ToArray(), this, default(CancellationToken));
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params int[] ids)
        {
            assertNotDisposed();
            return storageFor<T, int>().LoadManyAsync(ids, this, token);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<int> ids)
        {
            assertNotDisposed();
            return storageFor<T, int>().LoadManyAsync(ids.ToArray(), this, token);
        }




        public IReadOnlyList<T> LoadMany<T>(params long[] ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadMany(ids, this);
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<long> ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadMany(ids.ToArray(), this);

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params long[] ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids, this, default(CancellationToken));

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<long> ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids.ToArray(), this, default(CancellationToken));
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids, this, token);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<long> ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids.ToArray(), this, token);
        }




        public IReadOnlyList<T> LoadMany<T>(params Guid[] ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadMany(ids, this);
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<Guid> ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadMany(ids.ToArray(), this);

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params Guid[] ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids, this, default(CancellationToken));

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<Guid> ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids.ToArray(), this, default(CancellationToken));
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids, this, token);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<Guid> ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids.ToArray(), this, token);
        }





        public IJsonLoader Json => throw new NotImplementedException();
        public Storage.ITenant Tenant => throw new NotImplementedException();
        public Guid? VersionFor<TDoc>(TDoc entity)
        {
            return storageFor<TDoc>().VersionFor(entity, this);
        }

        public IReadOnlyList<TDoc> Search<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.Search(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> SearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.Search(searchTerm, regConfig)).ToListAsync(token: token);
        }

        public IReadOnlyList<TDoc> PlainTextSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.PlainTextSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> PlainTextSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.PlainTextSearch(searchTerm, regConfig)).ToListAsync(token: token);
        }

        public IReadOnlyList<TDoc> PhraseSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.PhraseSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> PhraseSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.PhraseSearch(searchTerm, regConfig)).ToListAsync(token: token);
        }

        public IReadOnlyList<TDoc> WebStyleSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.WebStyleSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> WebStyleSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.WebStyleSearch(searchTerm, regConfig)).ToListAsync(token: token);
        }
    }
}
