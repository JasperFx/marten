using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Events
{
    public class EventStreamVersioningCallback : ICallback
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

            // TODO -- write the sequences too
            _stream.ApplyLatestVersion(values[0]);
        }

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            await reader.ReadAsync(token).ConfigureAwait(false);

            var values = await reader.GetFieldValueAsync<int[]>(0, token).ConfigureAwait(false);

            // TODO -- write the sequences too
            _stream.ApplyLatestVersion(values[0]);
        }
    }
}