using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Weasel.Postgresql;

namespace Marten.PLv8.Transforms
{
    internal class DocumentTransformOperationFragment : IOperationFragment
    {
        private readonly IDocumentStorage _storage;
        private readonly TransformFunction _function;

        public DocumentTransformOperationFragment(IDocumentStorage storage, TransformFunction function)
        {
            _storage = storage;
            _function = function;
        }

        public void Apply(CommandBuilder sql)
        {
            var version = CombGuidIdGeneration.NewGuid();

            sql.Append("update ");
            sql.Append(_storage.TableName.QualifiedName);
            sql.Append(" as d set data = ");
            sql.Append(_function.Identifier.QualifiedName);
            sql.Append("(data), ");
            sql.Append(SchemaConstants.LastModifiedColumn);
            sql.Append(" = (now() at time zone 'utc'), ");
            sql.Append(SchemaConstants.VersionColumn);
            sql.Append(" = '");
            sql.Append(version.ToString());
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
