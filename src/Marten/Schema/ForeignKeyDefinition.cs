using System;
using System.Text;

namespace Marten.Schema
{
    public class ForeignKeyDefinition
    {
        private readonly string _columnName;
        private readonly DocumentMapping _parent;
        private readonly DocumentMapping _reference;
        private string _keyName;

        public ForeignKeyDefinition(string columnName, DocumentMapping parent, DocumentMapping reference)
        {
            _columnName = columnName;
            _parent = parent;
            _reference = reference;
        }

        public string KeyName
        {
            get { return _keyName ?? $"{_parent.Table.Name}_{_columnName}_fkey"; }
            set { _keyName = value; }
        }

        public string ColumnName => _columnName;

        public Type ReferenceDocumentType => _reference.DocumentType;

        public string ToDDL()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"ALTER TABLE {_parent.Table.QualifiedName}");
            sb.AppendLine($"ADD CONSTRAINT {KeyName} FOREIGN KEY ({_columnName})");
            sb.Append($"REFERENCES {_reference.Table.QualifiedName} ({_reference.IdMember.Name.ToLower()});");

            return sb.ToString();
        }
    }
}