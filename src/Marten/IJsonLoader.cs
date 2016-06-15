using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten
{
    public interface IJsonLoader
    {
        /// <summary>
        /// Load or find only the document json by string id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string FindById<T>(string id) where T : class;

        /// <summary>
        /// Load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string FindById<T>(int id) where T : class;

        /// <summary>
        /// Load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string FindById<T>(long id) where T : class;

        /// <summary>
        /// Load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string FindById<T>(Guid id) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by string id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string> FindByIdAsync<T>(string id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string> FindByIdAsync<T>(int id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string> FindByIdAsync<T>(long id, CancellationToken token = default(CancellationToken)) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string> FindByIdAsync<T>(Guid id, CancellationToken token = default(CancellationToken)) where T : class;
    }
}