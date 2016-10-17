using System;

namespace Marten
{
    public class PostgresqlIdentifierInvalidException : Exception
    {
        public string Name { get; set; }

        public PostgresqlIdentifierInvalidException(string name) 
            : base($"Database identifier {name} is not valid. See https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html for valid unquoted identifiers (Marten does not quote identifiers).")
        {
            Name = name;
        }
    }
}