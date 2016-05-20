using System.Text;

namespace Marten.Services
{
    public interface ICall
    {
        void WriteToSql(StringBuilder builder);
    }
}