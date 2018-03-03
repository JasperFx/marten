using System;
using System.Text;

namespace Marten.Schema
{
    public class ForeignKeyDefinition
    {
        private readonly DocumentMapping _parent;
        private readonly DocumentMapping _reference;
        private string _keyName;

        public ForeignKeyDefinition(string columnName, DocumentMapping parent, DocumentMapping reference)
        {
            ColumnName = columnName;
            _parent = parent;
            _reference = reference;
        }

        public string KeyName
        {
            get => _keyName ?? $"{_parent.Table.Name}_{ColumnName}_fkey";
            set => _keyName = value;
        }

        public string ColumnName { get; }

        public Type ReferenceDocumentType => _reference.DocumentType;

        public string ToDDL()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"ALTER TABLE {_parent.Table.QualifiedName}");
            sb.AppendLine($"ADD CONSTRAINT {KeyName} FOREIGN KEY ({ColumnName})");
            sb.Append($"REFERENCES {_reference.Table.QualifiedName} (id);");

            return sb.ToString();
        }
    }
}