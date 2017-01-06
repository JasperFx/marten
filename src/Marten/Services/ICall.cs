using System.Text;

namespace Marten.Services
{
    /// <summary>
    /// Marker interface telling Marten not
    /// to advance the results for callbacks
    /// </summary>
    public interface NoDataReturnedCall : ICall
    {
        
    }

    public interface ICall
    {
        void WriteToSql(StringBuilder builder);
    }
}