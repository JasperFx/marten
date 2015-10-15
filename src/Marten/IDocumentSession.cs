using System;
using System.Collections.Generic;
using Marten.Schema;
using Npgsql;

namespace Marten
{

    public interface IDocumentStore
    {
        IDocumentSchema Schema { get; }
        AdvancedOptions Advanced { get; }
    }

    public class AdvancedOptions
    {
        private readonly IDocumentCleaner _cleaner;

        public AdvancedOptions(IDocumentCleaner cleaner)
        {
            _cleaner = cleaner;
        }

        public IDocumentCleaner Clean
        {
            get { return _cleaner; }
        }
    }



	/// <summary>
	///     Interface for document session
	/// </summary>
	public interface IDocumentSession : IDisposable
	{
		void Delete<T>(T entity);
		void Delete<T>(ValueType id);
		void Delete(string id);

		T Load<T>(string id);
		T[] Load<T>(IEnumerable<string> ids);
		T Load<T>(ValueType id);
		T[] Load<T>(params ValueType[] ids);
		T[] Load<T>(IEnumerable<ValueType> ids);

		/// <summary>
		///     Saves all the pending changes to the server.
		/// </summary>
		void SaveChanges();

        // Store by etag? Version strategy?

		/// <summary>
		///     Stores entity in session, extracts Id from entity using Conventions or generates new one if it is not available.
		///     <para>Forces concurrency check if the Id is not available during extraction.</para>
		/// </summary>
		/// <param name="entity">entity to store.</param>
		void Store(object entity);
	}

    public class DocumentSession : IDocumentSession
    {
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly NpgsqlConnection _connection;

        public DocumentSession(IDocumentSchema schema, ISerializer serializer, NpgsqlConnection connection)
        {
            _schema = schema;
            _serializer = serializer;
            _connection = connection;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(T entity)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(ValueType id)
        {
            throw new NotImplementedException();
        }

        public void Delete(string id)
        {
            throw new NotImplementedException();
        }

        public T Load<T>(string id)
        {
            throw new NotImplementedException();
        }

        public T[] Load<T>(IEnumerable<string> ids)
        {
            throw new NotImplementedException();
        }

        public T Load<T>(ValueType id)
        {
            throw new NotImplementedException();
        }

        public T[] Load<T>(params ValueType[] ids)
        {
            throw new NotImplementedException();
        }

        public T[] Load<T>(IEnumerable<ValueType> ids)
        {
            throw new NotImplementedException();
        }

        public void SaveChanges()
        {
            throw new NotImplementedException();
        }

        public void Store(object entity)
        {
            throw new NotImplementedException();
        }
    }
}
