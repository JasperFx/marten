using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
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
        string? FindById<T>(string id) where T : class;

        /// <summary>
        /// Load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string? FindById<T>(int id) where T : class;

        /// <summary>
        /// Load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string? FindById<T>(long id) where T : class;

        /// <summary>
        /// Load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        string? FindById<T>(Guid id) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by string id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string?> FindByIdAsync<T>(string id, CancellationToken token = default) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string?> FindByIdAsync<T>(int id, CancellationToken token = default) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string?> FindByIdAsync<T>(long id, CancellationToken token = default) where T : class;

        /// <summary>
        /// Asynchronously load or find only the document json by numeric or Guid id for a document of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<string?> FindByIdAsync<T>(Guid id, CancellationToken token = default) where T : class;



        /// <summary>
        /// Write the raw persisted JSON for a single document found by id to the supplied stream. Returns false
        /// if the document cannot be found
        /// </summary>
        /// <param name="id"></param>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> StreamById<T>(int id, Stream destination, CancellationToken token = default) where T : class;

        /// <summary>
        /// Write the raw persisted JSON for a single document found by id to the supplied stream. Returns false
        /// if the document cannot be found
        /// </summary>
        /// <param name="id"></param>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> StreamById<T>(long id, Stream destination, CancellationToken token = default) where T : class;

        /// <summary>
        /// Write the raw persisted JSON for a single document found by id to the supplied stream. Returns false
        /// if the document cannot be found
        /// </summary>
        /// <param name="id"></param>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> StreamById<T>(string id, Stream destination, CancellationToken token = default) where T : class;

        /// <summary>
        /// Write the raw persisted JSON for a single document found by id to the supplied stream. Returns false
        /// if the document cannot be found
        /// </summary>
        /// <param name="id"></param>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> StreamById<T>(Guid id, Stream destination, CancellationToken token = default) where T : class;
    }
}
