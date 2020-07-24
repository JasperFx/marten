using System;
using Baseline;
using Marten.Linq.Fields;

namespace Marten.Internal.Linq
{
    public class JsonStatement : Statement
    {
        public JsonStatement(Type documentType, IFieldMapping fields, Statement parent) : base(typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(parent.ExportName,
            documentType), fields)
        {

        }

        private JsonStatement(ISelectClause selectClause, IFieldMapping fields) : base(selectClause, fields)
        {
        }


        public Statement CloneForTempTableCreation()
        {
            // Watch this!!!
            var clone = new JsonStatement(SelectClause, Fields)
            {
                Offset = Offset,
                Limit = Limit,
                Where = Where,
                Orderings = Orderings,
                Next = Next,
                Mode = Mode,
                ExportName = ExportName
            };

            clone.WhereClauses.AddRange(WhereClauses);

            return clone;
        }
    }
}
