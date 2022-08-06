#nullable enable
using System.Data.Common;
using Newtonsoft.Json.Linq;

namespace Marten.Services.Json.Transformations.JsonNet
{
    public abstract class EventUpcaster<TEvent>: Json.Transformations.EventUpcaster<TEvent>
        where TEvent : notnull
    {
        public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index) =>
            Transformations.FromDbDataReader(Upcast)(serializer, dbDataReader, index);

        protected abstract TEvent Upcast(JObject oldEvent);
    }
}
