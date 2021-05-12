using System.Threading.Tasks;

#nullable enable
namespace Marten.Schema
{
    /// <summary>
    /// A set of initial data to pre-populate a DocumentStore at startup time
    /// Users will have to be responsible for not duplicating data
    /// </summary>
    public interface IInitialData
    {
        /// <summary>
        /// Apply the data loading
        /// </summary>
        /// <param name="store"></param>
        Task Populate(IDocumentStore store);
    }
}
