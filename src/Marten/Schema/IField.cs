using System.Reflection;

namespace Marten.Schema
{
    public interface IField
    {
        MemberInfo[] Members { get; }
        string MemberName { get; }

        string SqlLocator { get; }
    }
}