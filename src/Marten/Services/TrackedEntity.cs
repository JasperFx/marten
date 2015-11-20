using System;
using Newtonsoft.Json.Linq;

namespace Marten.Services
{
    public class TrackedEntity
    {
        private readonly ISerializer _serializer;
        private string _json;

        public TrackedEntity(object id, ISerializer serializer, Type documentType, string json)
        {
            _serializer = serializer;
            Id = id;
            DocumentType = documentType;
            _json = json;

            Document = _serializer.FromJson(documentType, json);
        }

        public object Id { get; }
        public Type DocumentType { get; }

        public object Document { get; }


        public void ResetJson(string json)
        {
            _json = json;
        }

        public DocumentChange DetectChange()
        {
            var newJson = _serializer.ToJson(Document);
            if (!JToken.DeepEquals(JObject.Parse(_json), JObject.Parse(newJson)))
            {
                return new DocumentChange(this, newJson);
            }

            return null;
        }
    }
}