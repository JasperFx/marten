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
    }
}
