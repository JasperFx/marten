using System;
using System.Reflection;
using Marten.Linq.Operators;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.QueryHandlers
{
    internal class LinqConstants
    {
        internal static readonly string StatsColumn = "count(1) OVER() as total_rows";
        internal static readonly string IdListTableName = "mt_temp_id_list";

        internal static readonly ISelector<string> StringValueSelector =
            new ScalarStringSelectClause(String.Empty, String.Empty);

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


        internal static readonly string CONTAINS = nameof(string.Contains);
        internal static readonly PropertyInfo ArrayLength = typeof(Array).GetProperty(nameof(Array.Length));
    }
}
