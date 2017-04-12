using System;
using System.Collections.Generic;
using Baseline;
using Marten.Generation;
using Marten.Storage;
using Marten.Util;

namespace Marten.Schema
{
    public class SchemaObjects
    {
        public Type DocumentType { get; }
        public TableDefinition Table { get; }
        public FunctionBody Function { get; set; }
        public IDictionary<string, ActualIndex> ActualIndices { get; } = new Dictionary<string, ActualIndex>();
        public IList<string> ForeignKeys { get; set; }

        public SchemaObjects(Type documentType, TableDefinition table, ActualIndex[] actualIndices, FunctionBody function)
        {
            DocumentType = documentType;
            Table = table;
            Function = function;

            actualIndices.Each(x => ActualIndices.Add(x.Name, x));



        }


        public bool HasNone()
        {
            return Table == null;
        }
    }
}