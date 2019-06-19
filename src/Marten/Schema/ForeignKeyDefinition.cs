using System;
using System.Text;
using Baseline;

namespace Marten.Schema
{
    public class ForeignKeyDefinition
    {
        private readonly DocumentMapping _parent;
        private readonly DocumentMapping _reference;
        private string _keyName;
        private readonly Func<ForeignKeyDefinition, string> _fkeyTableRefFunc;
        private readonly Func<ForeignKeyDefinition, string> _fkeyColumnRefFunc;
        private readonly Func<ForeignKeyDefinition, string> _fkeyExtraFunc;

        public ForeignKeyDefinition(
            string columnName,
            DocumentMapping parent,
            DocumentMapping reference
        ) : this(
            columnName,
            parent,
            fkd => reference.Table.QualifiedName,
            fkd => "(id)",
            GenerateOnDeleteClause
        )
        {
            _reference = reference;
        }

        protected ForeignKeyDefinition(
            string columnName,
            DocumentMapping parent,
            Func<ForeignKeyDefinition, string> fkeyTableRefFunc,
            Func<ForeignKeyDefinition, string> fkeyColumnRefFunc,
            Func<ForeignKeyDefinition, string> fkeyExtraFunc
        )
        {
            ColumnName = columnName;
            _parent = parent;
            _fkeyTableRefFunc = fkeyTableRefFunc;
            _fkeyColumnRefFunc = fkeyColumnRefFunc;
            _fkeyExtraFunc = fkeyExtraFunc;
        }

        public string KeyName
        {
            get => _keyName ?? $"{_parent.Table.Name}_{ColumnName}_fkey";
            set => _keyName = value;
        }

        public string ColumnName { get; }

        public bool CascadeDeletes { get; set; }

        public Type ReferenceDocumentType => _reference?.DocumentType;

        public string ToDDL()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"ALTER TABLE {_parent.Table.QualifiedName}");
            sb.AppendLine($"ADD CONSTRAINT {KeyName} FOREIGN KEY ({ColumnName})");
            sb.Append($"REFERENCES {_fkeyTableRefFunc.Invoke(this)} {_fkeyColumnRefFunc.Invoke(this)}");

            var extra = _fkeyExtraFunc?.Invoke(this);
            if (extra.IsNotEmpty())
            {
                sb.AppendLine();
                sb.Append(extra);
            }

            sb.Append(";");
            return sb.ToString();
        }

        protected static string GenerateOnDeleteClause(ForeignKeyDefinition fkd) => fkd.CascadeDeletes ? "ON DELETE CASCADE" : string.Empty;
    }

    public class ExternalForeignKeyDefinition: ForeignKeyDefinition
    {
        public ExternalForeignKeyDefinition(
            string columnName,
            DocumentMapping parent,
            string externalSchemaName,
            string externalTableName,
            string externalColumnName
        ) : base(
            columnName,
            parent,
            _ => $"{externalSchemaName}.{externalTableName}",
            _ => $"({externalColumnName})",
            GenerateOnDeleteClause
        )
        {
        }
    }
}
