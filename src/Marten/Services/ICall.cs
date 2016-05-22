using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public interface ICall
    {
        void WriteToSql(StringBuilder builder);
    }

    public interface ICallback
    {
        void Postprocess(DbDataReader reader, IList<Exception> exceptions);
        Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);
    }
}