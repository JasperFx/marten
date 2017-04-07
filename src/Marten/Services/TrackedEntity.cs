using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Marten.Services
{
    public class TrackedEntity
    {
        private readonly ISerializer _serializer;
        private string _json;

        public TrackedEntity(object id, ISerializer serializer, Type documentType, object document, TextReader json)
        {
            _serializer = serializer;
            Id = id;
            DocumentType = documentType;
            Document = document;

            if (json != null && document == null)
            {
                Document = _serializer.FromJson(documentType, json);
            }
            else if (document != null)
            { 
                _json = _serializer.ToJson(document);
            }
        }

        public object Id { get; }
        public Type DocumentType { get; }

        public object Document { get; }
        public UnitOfWorkOrigin Origin { get; set; } = UnitOfWorkOrigin.Loaded;


        public void ResetJson(string json)
        {
            Origin = UnitOfWorkOrigin.Loaded;
            _json = json;
        }

        public DocumentChange DetectChange()
        {
            if (Document == null) return null;

            var newJson = _serializer.ToJson(Document);
            if (!JToken.DeepEquals(JObject.Parse(_json), JObject.Parse(newJson)))
            {
                return new DocumentChange(this, newJson);
            }

            return null;
        }
    }
}