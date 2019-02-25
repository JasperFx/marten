using System;
using System.Text;

namespace Marten.Schema
{
    public abstract class ForeignKeyDefinition
    {
        private string _keyName;

        protected ForeignKeyDefinition(string columnName, DocumentMapping parent)
        {
            ColumnName = columnName;
            Parent = parent;
        }

        protected DocumentMapping Parent { get; }

        public string KeyName
        {
            get => _keyName ?? $"{Parent.Table.Name}_{ColumnName}_fkey";
            set => _keyName = value;
        }

        public string ColumnName { get; }

        public abstract Type ReferenceDocumentType { get; }

        public abstract string ToDDL();
    }

    public class DocumentReferenceForeignKeyDefinition : ForeignKeyDefinition
    {
        private readonly DocumentMapping _reference;

        public DocumentReferenceForeignKeyDefinition(string columnName, DocumentMapping parent, DocumentMapping reference) : base(columnName, parent)
        {
            _reference = reference;
        }

        public override Type ReferenceDocumentType => _reference.DocumentType;

        public override string ToDDL()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"ALTER TABLE {Parent.Table.QualifiedName}");
            sb.AppendLine($"ADD CONSTRAINT {KeyName} FOREIGN KEY ({ColumnName})");
            sb.Append($"REFERENCES {_reference.Table.QualifiedName} (id);");

            return sb.ToString();
        }
    }

    public class ForeignReferenceForeignKeyDefinition : ForeignKeyDefinition
    {
        private readonly string _foreignSchemaName;
        private readonly string _foreignTableName;
        private readonly string _foreignColumnName;

        public ForeignReferenceForeignKeyDefinition(string columnName, DocumentMapping parent, string foreignSchemaName, string foreignTableName,
                                                    string foreignColumnName) : base(columnName, parent)
        {
            _foreignSchemaName = foreignSchemaName;
            _foreignTableName = foreignTableName;
            _foreignColumnName = foreignColumnName;
        }

        public override Type ReferenceDocumentType => Parent.DocumentType;

        public override string ToDDL()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"ALTER TABLE {Parent.Table.QualifiedName}");
            sb.AppendLine($"ADD CONSTRAINT {KeyName} FOREIGN KEY ({ColumnName})");
            sb.Append($"REFERENCES {_foreignSchemaName}.{_foreignTableName} ({_foreignColumnName});");

            return sb.ToString();
        }
    }
}