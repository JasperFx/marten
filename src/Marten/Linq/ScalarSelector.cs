using System;
using System.Data.Common;
using Baseline;
using Marten.Services;

namespace Marten.Linq
{
    public class ScalarSelector<TResult> : ISelector<TResult>
    {
        private static string[] _empty = {};

        public TResult Resolve(DbDataReader reader, IIdentityMap map)
        {
            var type = typeof (TResult);
            var result = default(TResult);
            if (reader.FieldCount == 0) return result;

            var value = reader.GetValue(0);
            if (type.IsNullable())
                return Convert.ChangeType(value, typeof (TResult).GetInnerTypeFromNullable()).As<TResult>();

            return Convert.ChangeType(reader.GetValue(0), typeof (TResult)).As<TResult>();
        }

        public string[] SelectFields()
        {
            return _empty;
        }
    }
}