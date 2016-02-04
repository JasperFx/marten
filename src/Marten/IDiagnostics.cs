using System.Data;
using System.Linq;

namespace Marten
{
    public interface IDiagnostics
    {
        /// <summary>
        /// Generates the NpgsqlCommand object that would be used to execute a Linq query. Use this
        /// to preview SQL or trouble shoot problems
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="queryable"></param>
        /// <returns></returns>
        IDbCommand CommandFor<T>(IQueryable<T> queryable);

        /// <summary>
        /// Returns the dynamic C# code that will be generated for the document type. Useful to understand
        /// the internal behavior of Marten for a single document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        string DocumentStorageCodeFor<T>();
    }
}