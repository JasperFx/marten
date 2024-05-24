using System.Linq.Expressions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public interface IComparableMember
{
    ISqlFragment CreateComparison(string op, ConstantExpression constant);
}
