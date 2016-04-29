
using System;

namespace Marten.Schema
{
    public class TableName : QualifiedDatabaseName
    {
        public TableName(string schema, string name) : base(schema, name)
        {
        }

        public static TableName Parse(string qualifiedName)
        {
            var parts = ParseQualifiedName(qualifiedName);
            return new TableName(parts[0], parts[1]);
        }

        /// <summary>
        /// Name that would match the relowner column in idx.indrelid::regclass
        /// </summary>
        public string OwnerName => Schema == StoreOptions.DefaultDatabaseSchemaName ? Name : QualifiedName;
    }

    public class FunctionName : QualifiedDatabaseName
    {
        public FunctionName(string schema, string name) : base(schema, name)
        {
        }

        public static FunctionName Parse(string qualifiedName)
        {
            var parts = ParseQualifiedName(qualifiedName);
            return new FunctionName(parts[0], parts[1]);
        }
    }

    public abstract class QualifiedDatabaseName
    {
        public string Schema { get; }
        public string Name { get; }
        public string QualifiedName => $"{Schema}.{Name}";

        public QualifiedDatabaseName(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }

        public override string ToString()
        {
            return QualifiedName;
        }

        protected bool Equals(QualifiedDatabaseName other)
        {
            return GetType() == other.GetType() && string.Equals(QualifiedName, other.QualifiedName, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((QualifiedDatabaseName)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return GetType().GetHashCode() * 397 ^ (QualifiedName?.GetHashCode() ?? 0);
            }
        }

        protected static string[] ParseQualifiedName(string qualifiedName)
        {
            var parts = qualifiedName.Split('.');
            if (parts.Length != 2)
            {
                throw new InvalidOperationException(
                    $"Could not parse QualifiedName: '{qualifiedName}'. Number or parts should be 2s but is {parts.Length}");
            }
            return parts;
        }
    }
}