using System;

namespace Marten
{
    public class PostgresqlIdentifierTooLongException : Exception
    {
        public int Length { get; set; }
        public string Name { get; set; }

        public PostgresqlIdentifierTooLongException(int length, string name) 
            : base($"Database identifier {name} would be truncated. The {nameof(StoreOptions)}{nameof(StoreOptions.NameDataLength)} property is currently {length}. You may want to change this value with a corresponding change to Postgresql's NAMEDATALEN")
        {
            Length = length;
            Name = name;
        }
    }
}