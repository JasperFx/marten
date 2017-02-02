using Marten.Util;

namespace Marten.Linq
{
    public interface IWhereFragment
    {
        void Apply(CommandBuilder builder);
        bool Contains(string sqlText);
    }
}