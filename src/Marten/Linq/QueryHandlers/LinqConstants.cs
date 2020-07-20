using Marten.Internal.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.QueryHandlers
{
    internal class LinqConstants
    {
        internal static readonly string StatsColumn = "count(1) OVER() as total_rows";
        internal static readonly string IdListTableName = "mt_temp_id_list";

        internal static readonly ISelector<string> StringValueSelector =
            new ScalarStringSelectClause(string.Empty, string.Empty);

        internal static readonly ResultOperatorBase AnyOperator = new AnyResultOperator();
        internal static readonly ResultOperatorBase CountOperator = new CountResultOperator();
        internal static readonly ResultOperatorBase LongCountOperator = new LongCountResultOperator();
        internal static readonly ResultOperatorBase SumOperator = new SumResultOperator();
        internal static readonly ResultOperatorBase MinOperator = new MinResultOperator();
        internal static readonly ResultOperatorBase MaxOperator = new MaxResultOperator();
        internal static readonly ResultOperatorBase AverageOperator = new AverageResultOperator();
        internal static readonly ResultOperatorBase AsJsonOperator = new AsJsonResultOperator(null);

        internal static readonly ResultOperatorBase FirstOperator = new FirstResultOperator(false);
        internal static readonly ResultOperatorBase SingleOperator = new SingleResultOperator(false);
        internal static readonly ResultOperatorBase FirstOrDefaultOperator = new FirstResultOperator(true);
        internal static readonly ResultOperatorBase SingleOrDefaultOperator = new SingleResultOperator(true);


    }
}
