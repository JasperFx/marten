using Marten.Internal.Operations;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Util;

namespace Marten.Transforms
{
    internal class DocumentTransformOperationFragment : IOperationFragment
    {
        private readonly IDocumentMapping _mapping;
        private readonly TransformFunction _function;

        public DocumentTransformOperationFragment(IDocumentMapping mapping, TransformFunction function)
        {
            _mapping = mapping;
            _function = function;
        }

        public void Apply(CommandBuilder sql)
        {
            var version = CombGuidIdGeneration.NewGuid();

            sql.Append("update ");
            sql.Append(_mapping.TableName.QualifiedName);
            sql.Append(" as d set data = ");
            sql.Append(_function.Identifier.QualifiedName);
            sql.Append("(data), ");
            sql.Append(SchemaConstants.LastModifiedColumn);
            sql.Append(" = (now() at time zone 'utc'), ");
            sql.Append(SchemaConstants.VersionColumn);
            sql.Append(" = '");
            sql.Append(version);
            sql.Append("'");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }

        public OperationRole Role()
        {
            return OperationRole.Other;
        }
    }
}
