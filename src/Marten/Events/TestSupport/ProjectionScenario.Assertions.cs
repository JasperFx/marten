using System;
using System.Threading.Tasks;
using LamarCodeGeneration;

namespace Marten.Events.TestSupport
{
    public partial class ProjectionScenario
    {
        /// <summary>
        /// General hook to run
        /// </summary>
        /// <param name="description"></param>
        /// <param name="assertions"></param>
        public void AssertAgainstProjectedData(string description, Func<IQuerySession, Task> assertions)
        {
            assertion(assertions).Description = description;
        }

        /// <summary>
        ///     Verify that a document with the supplied id exists
        /// </summary>
        /// <param name="id"></param>
        /// <param name="assertions">Optional lambda to make additional assertions about the document state</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldExist<T>(string id, Action<T> assertions = null)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document == null)
                {
                    throw new Exception($"Document {typeof(T).FullNameInCode()} with id '{id}' does not exist");
                }

                assertions?.Invoke(document);
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should exist";
        }

        /// <summary>
        ///     Verify that a document with the supplied id exists
        /// </summary>
        /// <param name="id"></param>
        /// <param name="assertions">Optional lambda to make additional assertions about the document state</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldExist<T>(long id, Action<T> assertions = null)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document == null)
                {
                    throw new Exception($"Document {typeof(T).FullNameInCode()} with id '{id}' does not exist");
                }

                assertions?.Invoke(document);
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should exist";
        }

        /// <summary>
        ///     Verify that a document with the supplied id exists
        /// </summary>
        /// <param name="id"></param>
        /// <param name="assertions">Optional lambda to make additional assertions about the document state</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldExist<T>(int id, Action<T> assertions = null)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document == null)
                {
                    throw new Exception($"Document {typeof(T).FullNameInCode()} with id '{id}' does not exist");
                }

                assertions?.Invoke(document);
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should exist";
        }

        /// <summary>
        ///     Verify that a document with the supplied id exists
        /// </summary>
        /// <param name="id"></param>
        /// <param name="assertions">Optional lambda to make additional assertions about the document state</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldExist<T>(Guid id, Action<T> assertions = null)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document == null)
                {
                    throw new Exception($"Document {typeof(T).FullNameInCode()} with id '{id}' does not exist");
                }

                assertions?.Invoke(document);
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should exist";
        }

        /// <summary>
        ///     Asserts that a document with a given id has been deleted or does not exist
        /// </summary>
        /// <param name="id">The identity of the document</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldNotExist<T>(string id)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document != null)
                {
                    throw new Exception(
                        $"Document {typeof(T).FullNameInCode()} with id '{id}' exists, but should not.");
                }
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should not exist or be deleted";
        }

        /// <summary>
        ///     Asserts that a document with a given id has been deleted or does not exist
        /// </summary>
        /// <param name="id">The identity of the document</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldNotExist<T>(long id)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document != null)
                {
                    throw new Exception(
                        $"Document {typeof(T).FullNameInCode()} with id '{id}' exists, but should not.");
                }
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should not exist or be deleted";
        }

        /// <summary>
        ///     Asserts that a document with a given id has been deleted or does not exist
        /// </summary>
        /// <param name="id">The identity of the document</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldNotExist<T>(int id)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document != null)
                {
                    throw new Exception(
                        $"Document {typeof(T).FullNameInCode()} with id '{id}' exists, but should not.");
                }
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should not exist or be deleted";
        }

        /// <summary>
        ///     Asserts that a document with a given id has been deleted or does not exist
        /// </summary>
        /// <param name="id">The identity of the document</param>
        /// <typeparam name="T">The document type</typeparam>
        public void DocumentShouldNotExist<T>(Guid id)
        {
            assertion(async session =>
            {
                var document = await session.LoadAsync<T>(id);
                if (document != null)
                {
                    throw new Exception(
                        $"Document {typeof(T).FullNameInCode()} with id '{id}' exists, but should not.");
                }
            }).Description = $"Document {typeof(T).FullNameInCode()} with id '{id}' should not exist or be deleted";
        }
    }
}
