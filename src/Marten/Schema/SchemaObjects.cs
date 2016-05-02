using System;
using System.Collections.Generic;
using Baseline;
using Marten.Generation;
using Marten.Util;

namespace Marten.Schema
{
    public class SchemaObjects
    {
        public Type DocumentType { get; }
        public TableDefinition Table { get; }
        public IDictionary<string, ActualIndex> ActualIndices { get; } = new Dictionary<string, ActualIndex>();
        public string UpsertFunction { get; }

        public SchemaObjects(Type documentType, TableDefinition table, ActualIndex[] actualIndices, string upsertFunction, List<string> drops)
        {
            DocumentType = documentType;
            Table = table;

            actualIndices.Each(x => ActualIndices.Add(x.Name, x));

            UpsertFunction = upsertFunction?.CanonicizeSql();

            FunctionDropStatements = drops;
        }

        public List<string> FunctionDropStatements { get; }

        public bool HasNone()
        {
            return Table == null;
        }
    }
}