#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Linq;

internal interface IMartenLinqQueryable
{
    MartenLinqQueryProvider MartenProvider { get; }
    QuerySession Session { get; }

    Expression Expression { get; }
    LinqQueryParser BuildLinqParser();
}

internal class MartenLinqQueryable<T>: IOrderedQueryable<T>, IMartenQueryable<T>, IMartenLinqQueryable
{
    public MartenLinqQueryable(QuerySession session, MartenLinqQueryProvider provider, Expression expression)
    {
        Provider = provider;
        Session = session;
        MartenProvider = provider;
        Expression = expression;
    }

    public MartenLinqQueryable(QuerySession session)
    {
        Session = session;
        MartenProvider = new MartenLinqQueryProvider(session, typeof(T));
        Provider = MartenProvider;
        Expression = Expression.Constant(this);
    }

    public MartenLinqQueryable(QuerySession session, Expression expression) : this(session, new MartenLinqQueryProvider(session, typeof(T)), expression)
    {

    }

    public IEnumerator<T> GetEnumerator ()
    {
        return Provider.Execute<IEnumerable<T>> (Expression).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator ()
    {
        return Provider.Execute<IEnumerable> (Expression).GetEnumerator();
    }

    public Type ElementType => typeof(T);
    public IQueryProvider Provider { get; }

    public MartenLinqQueryProvider MartenProvider { get; }
    public Expression Expression { get; }

    public QuerySession Session { get; }

    public LinqQueryParser BuildLinqParser()
    {
        return new LinqQueryParser(MartenProvider, Session, Expression);
    }

    public QueryStatistics Statistics
    {
        get => MartenProvider.Statistics;
        set => MartenProvider.Statistics = value;
    }

    public async Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token)
    {
        var builder = new LinqQueryParser(MartenProvider, Session, Expression);
        var handler = builder.BuildListHandler<TResult>();

        await MartenProvider.EnsureStorageExistsAsync(builder, token).ConfigureAwait(false);

        return await MartenProvider.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken token = default)
    {
        return MartenProvider.ExecuteAsyncEnumerable<T>(Expression, token);
    }

    public Task<bool> AnyAsync(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<bool>(Expression, token, SingleValueMode.Any);
    }

    public Task<int> CountAsync(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<int>(Expression, token, SingleValueMode.Count);
    }

    public Task<long> CountLongAsync(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<long>(Expression, token, SingleValueMode.LongCount);
    }

    public Task<TResult> FirstAsync<TResult>(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<TResult>(Expression, token, SingleValueMode.First);
    }

    public Task<TResult?> FirstOrDefaultAsync<TResult>(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<TResult>(Expression, token, SingleValueMode.FirstOrDefault)!;
    }

    public Task<TResult> SingleAsync<TResult>(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<TResult>(Expression, token, SingleValueMode.Single);
    }

    public Task<TResult?> SingleOrDefaultAsync<TResult>(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<TResult>(Expression, token, SingleValueMode.SingleOrDefault)!;
    }

    public Task<TResult> SumAsync<TResult>(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<TResult>(Expression, token, SingleValueMode.Sum);
    }

    public Task<TResult> MinAsync<TResult>(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<TResult>(Expression, token, SingleValueMode.Min);
    }

    public Task<TResult> MaxAsync<TResult>(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<TResult>(Expression, token, SingleValueMode.Max);
    }

    public Task<double> AverageAsync(CancellationToken token)
    {
        return MartenProvider.ExecuteAsync<double>(Expression, token, SingleValueMode.Average);
    }

    public QueryPlan Explain(FetchType fetchType = FetchType.FetchMany,
        Action<IConfigureExplainExpressions>? configureExplain = null)
    {
        var command = ToPreviewCommand(fetchType);

        using var conn = Session.Database.CreateConnection();
        conn.Open();
        command.Connection = conn;
        return conn.ExplainQuery(Session.Serializer, command, configureExplain)!;
    }



    public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback)
        where TInclude : notnull
    {
        var include = BuildInclude(idSource, callback);
        return this.IncludePlan(include);
    }

    public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list)
        where TInclude : notnull
    {
        return Include<TInclude>(idSource, list.Add);
    }

    public IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
        IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey : notnull
    {
        var include = BuildInclude(idSource, dictionary);
        return this.IncludePlan(include);
    }

    public IMartenQueryable<T> Stats(out QueryStatistics stats)
    {
        Statistics = new QueryStatistics();
        stats = Statistics;

        return this;
    }

    internal IQueryHandler<TResult> BuildHandler<TResult>(SingleValueMode? mode = null)
    {
        var parser = new LinqQueryParser(MartenProvider, Session, Expression, mode);
        return parser.BuildHandler<TResult>();
    }

    public Task<int> StreamJsonArray(Stream destination, CancellationToken token)
    {
        return MartenProvider.StreamMany(Expression, destination, token);
    }

    internal IIncludePlan BuildInclude<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback)
        where TInclude : notnull
    {
        var storage = (IDocumentStorage<TInclude>)Session.StorageFor(typeof(TInclude));
        var identityMember = Session.StorageFor(typeof(T)).QueryMembers.MemberFor(idSource);

        var include = new IncludePlan<TInclude>(storage, identityMember, callback);
        return include;
    }

    internal IIncludePlan BuildInclude<TInclude, TKey>(Expression<Func<T, object>> idSource,
        IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey : notnull
    {
        var storage = (IDocumentStorage<TInclude>)Session.StorageFor(typeof(TInclude));

        if (storage is IDocumentStorage<TInclude, TKey> s)
        {
            var identityMember = Session.StorageFor(typeof(T)).QueryMembers.MemberFor(idSource);

            void Callback(TInclude item)
            {
                var id = s.Identity(item);
                dictionary[id] = item;
            }

            return new IncludePlan<TInclude>(storage, identityMember, Callback);
        }

        throw new DocumentIdTypeMismatchException(storage, typeof(TKey));
    }

    public NpgsqlCommand ToPreviewCommand(FetchType fetchType)
    {
        var parser = new LinqQueryParser(MartenProvider, Session, Expression);

        var command = new NpgsqlCommand();

        var sql = new CommandBuilder(command);

        parser.BuildDiagnosticCommand(fetchType, sql);
        command.CommandText = sql.ToString();

        foreach (var documentType in parser.DocumentTypes()) Session.Database.EnsureStorageExists(documentType);

        Session._connection.Apply(command);

        return command;
    }

    public async Task<string> ToJsonArray(CancellationToken token)
    {
        var stream = new MemoryStream();
        await StreamJsonArray(stream, token).ConfigureAwait(false);
        stream.Position = 0;
        return await stream.ReadAllTextAsync().ConfigureAwait(false);
    }

    public Task StreamJsonFirst(Stream destination, CancellationToken token)
    {
        return MartenProvider.StreamJson<T>(destination, Expression, token, SingleValueMode.First);
    }

    public Task<int> StreamJsonFirstOrDefault(Stream destination, CancellationToken token)
    {
        return MartenProvider.StreamJson<T>(destination, Expression, token, SingleValueMode.FirstOrDefault);
    }

    public Task StreamJsonSingle(Stream destination, CancellationToken token)
    {
        return MartenProvider.StreamJson<T>(destination, Expression, token, SingleValueMode.Single);
    }

    public Task<int> StreamJsonSingleOrDefault(Stream destination, CancellationToken token)
    {
        return MartenProvider.StreamJson<T>(destination, Expression, token, SingleValueMode.SingleOrDefault);
    }

    public async Task<string> ToJsonFirst(CancellationToken token)
    {
        var stream = new MemoryStream();
        await StreamJsonFirst(stream, token).ConfigureAwait(false);
        stream.Position = 0;
        return await stream.ReadAllTextAsync().ConfigureAwait(false);
    }

    public async Task<string?> ToJsonFirstOrDefault(CancellationToken token)
    {
        var stream = new MemoryStream();
        var actual = await StreamJsonFirstOrDefault(stream, token).ConfigureAwait(false);
        if (actual == 0)
        {
            return null;
        }

        stream.Position = 0;
        return await stream.ReadAllTextAsync().ConfigureAwait(false);
    }

    public async Task<string> ToJsonSingle(CancellationToken token)
    {
        var stream = new MemoryStream();
        await StreamJsonSingle(stream, token).ConfigureAwait(false);
        stream.Position = 0;
        return await stream.ReadAllTextAsync().ConfigureAwait(false);
    }

    public async Task<string?> ToJsonSingleOrDefault(CancellationToken token)
    {
        var stream = new MemoryStream();
        var count = await StreamJsonSingleOrDefault(stream, token).ConfigureAwait(false);
        if (count == 0)
        {
            return null;
        }

        stream.Position = 0;
        return await stream.ReadAllTextAsync().ConfigureAwait(false);
    }
}
