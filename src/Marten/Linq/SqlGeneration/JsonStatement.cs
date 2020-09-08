using System;
using Baseline;
using Marten.Linq.Fields;
using Marten.Linq.Includes;

namespace Marten.Linq.SqlGeneration
{
    public class JsonStatement : SelectorStatement
    {
        public JsonStatement(Type documentType, IFieldMapping fields, Statement parent) : base(typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(parent.ExportName,
            documentType), fields)
        {

        }

        private JsonStatement(ISelectClause selectClause, IFieldMapping fields) : base(selectClause, fields)
        {
        }


        public override SelectorStatement UseAsEndOfTempTableAndClone(IncludeIdentitySelectorStatement includeIdentitySelectorStatement)
        {
            includeIdentitySelectorStatement.IncludeDataInTempTable = true;

            var clone = new JsonStatement(SelectClause, Fields)
            {
                SelectClause = SelectClause.As<IScalarSelectClause>().CloneToOtherTable(includeIdentitySelectorStatement.ExportName),
                Orderings = Orderings,
                Mode = StatementMode.Select,
                ExportName = ExportName
            };

            SelectClause = includeIdentitySelectorStatement;

            Limit = Offset = 0;

            return clone;
        }
    }
}
