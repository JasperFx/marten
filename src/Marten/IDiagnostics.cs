using System.Data;
using System.Linq;

namespace Marten
{
    public interface IDiagnostics
    {

        /// <summary>
        /// Returns the dynamic C# code that will be generated for the document type. Useful to understand
        /// the internal behavior of Marten for a single document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        string DocumentStorageCodeFor<T>();
    }
}