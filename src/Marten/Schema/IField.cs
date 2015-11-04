using System.Reflection;
using Remotion.Linq.Clauses;

namespace Marten.Schema
{
    public interface IField
    {
        MemberInfo[] Members { get; }
        string MemberName { get; }

        string SqlLocator { get; }
        string LateralJoinDeclaration { get; }
    }
}