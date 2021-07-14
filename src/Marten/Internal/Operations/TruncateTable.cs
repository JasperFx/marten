using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Util;
using Weasel.Core;

namespace Marten.Internal.Operations
{
    internal class TruncateTable: IStorageOperation
    {
        private readonly DbObjectName _name;

        public TruncateTable(DbObjectName name)
        {
            _name = name;
        }

        public TruncateTable(Type documentType)
        {
            DocumentType = documentType;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var name = _name ?? session.StorageFor(DocumentType).TableName;
            builder.Append($"truncate table {name} CASCADE");
        }

        public Type DocumentType { get; }

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // nothing
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public OperationRole Role()
        {
            return OperationRole.Other;
        }

        protected bool Equals(TruncateTable other)
        {
            return Equals(DocumentType, other.DocumentType);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((TruncateTable) obj);
        }

        public override int GetHashCode()
        {
            return (DocumentType != null ? DocumentType.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return $"Truncate data for: {DocumentType}";
        }
    }
}
