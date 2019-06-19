using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Events
{
    public class EventStreamVersioningCallback: ICallback
    {
        private readonly EventStream _stream;

        public EventStreamVersioningCallback(EventStream stream)
        {
            _stream = stream;
        }

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            reader.Read();
            var values = reader.GetFieldValue<int[]>(0);

            applyDataFromSproc(values);
        }

        private void applyDataFromSproc(int[] values)
        {
            _stream.ApplyLatestVersion(values[0]);

            for (int i = 1; i < values.Length; i++)
            {
                _stream.Events.ElementAt(i - 1).Sequence = values[i];
            }
        }

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            await reader.ReadAsync(token).ConfigureAwait(false);

            var values = await reader.GetFieldValueAsync<int[]>(0, token).ConfigureAwait(false);

            applyDataFromSproc(values);
        }
    }
}
