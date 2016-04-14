using System;
using System.Data.Common;
using Baseline;
using Marten.Services;

namespace Marten.Linq
{
    [Obsolete("This is going to problem w/ the new select clause mechanisms")]
    public class ScalarSelector<TResult> : BasicSelector, ISelector<TResult>
    {
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

    }
}