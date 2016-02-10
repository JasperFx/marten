using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Npgsql;
using Remotion.Linq;

namespace Marten.Services.BatchQuerying
{


    public class AnyHandler : IDataReaderHandler<bool>
    {
        private readonly TaskCompletionSource<bool> _source = new TaskCompletionSource<bool>(); 

        public void Configure(NpgsqlCommand command, DocumentQuery query)
        {
            query.ConfigureForAny(command);
        }

        public Task<bool> ReturnValue => _source.Task;

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            await reader.ReadAsync(token).ConfigureAwait(false);

            _source.SetResult(reader.GetBoolean(0));
        }
    }
}