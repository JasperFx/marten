using System;
using System.Linq.Expressions;
using Marten.Schema;

namespace Marten.Storage
{
    public static class StorageFeaturesExtensions
    {
        public static string ColumnName<T>(this StorageFeatures storageFeatures, Expression<Func<T, object>> e)
        {
            return storageFeatures
                .MappingFor(typeof(T)).FieldInfo(e).ColumnName;
        }
    }
}