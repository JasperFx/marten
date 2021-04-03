using System;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Fields;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Linq.Includes
{
    public class IncludePlan<T> : IIncludePlan
    {
        private readonly IDocumentStorage<T> _storage;
        private readonly Action<T> _callback;

        public IncludePlan(IDocumentStorage<T> storage, IField connectingField, Action<T> callback)
        {
            _storage = storage;
            ConnectingField = connectingField;
            _callback = callback;
        }

        public IField ConnectingField { get; }

        public int Index
        {
            set
            {
                IdAlias = "id" + (value + 1);
                ExpressionName = "include"+ (value + 1);

                TempTableSelector = RequiresLateralJoin()
                    ? $"{ExpressionName}.{IdAlias}"
                    : $"{ConnectingField.LocatorForIncludedDocumentId} as {IdAlias}";
            }
        }

        public bool RequiresLateralJoin()
        {
            // TODO -- dont' think this is permanent. Or definitely shouldn't be
            return ConnectingField is ArrayField;
        }

        public string LeftJoinExpression => $"LEFT JOIN LATERAL {ConnectingField.LocatorForIncludedDocumentId} WITH ORDINALITY as {ExpressionName}({IdAlias}) ON TRUE";

        public string ExpressionName { get; private set; }

        public string IdAlias { get; private set; }
        public string TempTableSelector { get; private set; }

        public Statement BuildStatement(string tempTableName)
        {
            return new IncludedDocumentStatement(_storage, this, tempTableName);
        }

        public IIncludeReader BuildReader(IMartenSession session)
        {
            var selector = (ISelector<T>) _storage.BuildSelector(session);
            return new IncludeReader<T>(_callback, selector);
        }

        public class IncludedDocumentStatement : SelectorStatement
        {
            public IncludedDocumentStatement(IDocumentStorage<T> storage, IncludePlan<T> includePlan,
                string tempTableName) : base(storage, storage.Fields)
            {
                var initial = new InTempTableWhereFragment(tempTableName, includePlan.IdAlias);
                Where = storage.FilterDocuments(null, initial);
            }

            protected override void configure(CommandBuilder sql)
            {
                base.configure(sql);
                sql.Append(";\n");
            }
        }
    }


}
