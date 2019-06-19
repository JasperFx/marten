using System;

namespace Marten.Services
{
    public class DocumentChange
    {
        private readonly TrackedEntity _entity;

        public DocumentChange(TrackedEntity entity, string json)
        {
            _entity = entity;
            Json = json;
        }

        public object Id => _entity.Id;

        public Type DocumentType => _entity.DocumentType;

        public string Json { get; }

        public object Document => _entity.Document;

        public void ChangeCommitted()
        {
            _entity.ResetJson(Json);
        }
    }
}
