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
            var current = reader.GetFieldValue<int>(0);

            _stream.ApplyLatestVersion(current);
        }

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            await reader.ReadAsync(token).ConfigureAwait(false);

            var current = await reader.GetFieldValueAsync<int>(0, token).ConfigureAwait(false);

            _stream.ApplyLatestVersion(current);
        }
    }
}