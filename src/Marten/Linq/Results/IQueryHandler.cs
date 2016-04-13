using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Npgsql;

namespace Marten.Linq.Results
{
    public interface IQueryHandler<T>
    {
        Type SourceType { get; }

        // It's done this way so that the same query handler can swing back
        // and forth between batched queries and standalone queries
        void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command);

        // Sync
        T Handle(DbDataReader reader, IIdentityMap map);

        // Async
        //Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token);
    }

    public class ListQueryHandler<T> : IQueryHandler<IList<T>>
    {
        private readonly DocumentQuery _query;
        private readonly ISelector<T> _selector;

        public ListQueryHandler(DocumentQuery query, ISelector<T> selector)
        {
            _query = query;
            _selector = selector;
        }

        public Type SourceType => _query.SourceDocumentType;
        public void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            _query.ConfigureCommand<T>(schema, command);
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            var list = new List<T>();

            while (reader.Read())
            {
                list.Add(_selector.Resolve(reader, map));
            }

            return list;
        }
    }

    public class AnyQueryHandler<T> : IQueryHandler<bool>
    {
        private readonly DocumentQuery _query;

        public AnyQueryHandler(DocumentQuery query)
        {
            _query = query;
        }

        public Type SourceType => typeof (T);

        public void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            _query.ConfigureForAny(command);
        }

        public bool Handle(DbDataReader reader, IIdentityMap map)
        {
            reader.Read();

            return reader.GetBoolean(0);
        }
    }

    public class CountQueryHandler<T> : IQueryHandler<long>
    {
        private readonly DocumentQuery _query;

        public CountQueryHandler(DocumentQuery query)
        {
            _query = query;
        }

        public Type SourceType => typeof(T);

        public void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            _query.ConfigureForCount(command);
        }

        public long Handle(DbDataReader reader, IIdentityMap map)
        {
            return reader.GetInt64(0);
        }
    }

    /*
    X Any
    X Count, LongCount
    X ToList
    X Single
    X SingleOrDefault
    X First
    X FirstOrDefault
    Aggregate functions

    */


    public class FirstHandler<T> : OnlyOneResultHandler<T>
    {
        public FirstHandler(DocumentQuery query, ISelector<T> selector) : base(query, selector)
        {
        }

        public override void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            throw new NotImplementedException();
        }

        protected override T defaultValue()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contained no elements");
        }
    }

    public class FirstOrDefaultHandler<T> : OnlyOneResultHandler<T>
    {
        public FirstOrDefaultHandler(DocumentQuery query, ISelector<T> selector) : base(query, selector)
        {
        }

        public override void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            throw new NotImplementedException();
        }
    }

    public class SingleHandler<T> : OnlyOneResultHandler<T>
    {
        public SingleHandler(DocumentQuery query, ISelector<T> selector) : base(query, selector)
        {
        }

        public override void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            throw new NotImplementedException();
        }

        protected override void assertMoreResults()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contains more than one element");
        }

        protected override T defaultValue()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contained no elements");
        }
    }

    public class SingleOrDefaultHandler<T> : OnlyOneResultHandler<T>
    {
        public SingleOrDefaultHandler(DocumentQuery query, ISelector<T> selector) : base(query, selector)
        {
        }

        public override void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            throw new NotImplementedException();
        }

        protected override void assertMoreResults()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contains more than one element");
        }
    }


}