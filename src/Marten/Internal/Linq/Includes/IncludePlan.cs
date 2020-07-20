using System;
using Marten.Internal.Storage;
using Marten.Linq.Fields;
using Marten.Util;

namespace Marten.Internal.Linq.Includes
{
    public class IncludePlan<T> : IIncludePlan
    {
        private readonly IDocumentStorage<T> _storage;
        private readonly Action<T> _callback;
        private readonly string _tempTableName;

        public IncludePlan(int index, IDocumentStorage<T> storage, IField connectingField, Action<T> callback)
        {
            _storage = storage;
            _callback = callback;

            IdAlias = "id" + (index + 1);

            TempSelector = $"{connectingField.TypedLocator} as {IdAlias}";
        }

        public string IdAlias { get;}
        public string TempSelector { get; }
        public Statement BuildStatement(string tempTableName)
        {
            return new IncludedDocumentStatement(_storage, this, tempTableName);
        }

        public IIncludeReader BuildReader(IMartenSession session)
        {
            var selector = (ISelector<T>) _storage.BuildSelector(session);
            return new IncludeReader<T>(_callback, selector);
        }

        public class IncludedDocumentStatement : Statement
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
