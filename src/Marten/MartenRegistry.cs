using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Marten.Schema;

namespace Marten
{
    public class MartenRegistry
    {
        private readonly IList<Action<IDocumentSchema>> _alterations = new List<Action<IDocumentSchema>>();

        public DocumentMappingExpression<T> For<T>()
        {
            return new DocumentMappingExpression<T>(this);
        } 

        private Action<IDocumentSchema> alter
        {
            set { _alterations.Add(value); }
        }

        public PostgresUpsertType UpsertType
        {
            set { alter = x => x.UpsertType = value; }
        }

        internal void Alter(IDocumentSchema schema)
        {
            _alterations.Each(x => x(schema));
        }

        public void Include<T>() where T : MartenRegistry, new()
        {
            alter = x => new T().Alter(x);
        }

        public void Include(MartenRegistry registry)
        {
            alter = registry.Alter;
        }

        public class DocumentMappingExpression<T>
        {
            private readonly MartenRegistry _parent;

            public DocumentMappingExpression(MartenRegistry parent)
            {
                _parent = parent;

                _parent.alter = schema => schema.MappingFor(typeof (T));
            }

            public DocumentMappingExpression<T> PropertySearching(PropertySearching searching)
            {
                alter = m => m.PropertySearching = searching; 
                return this;
            }  

            public DocumentMappingExpression<T> Searchable(Expression<Func<T, object>> expression, Action<IndexDefinition> configureIndex = null)
            {
                var visitor = new FindMembers();
                visitor.Visit(expression);

                alter = mapping =>
                {
                   var index =  mapping.DuplicateField(visitor.Members.ToArray());
                    configureIndex?.Invoke(index);
                };

                return this;
            }   

            public DocumentMappingExpression<T> ConfigureUpsertType(PostgresUpsertType upsertType)
            {
                _parent._alterations.Add(s => s.UpsertType = upsertType);
                
                return this;
            }

            private Action<DocumentMapping> alter
            {
                set
                {
                    Action<IDocumentSchema> alteration = schema => { value(schema.MappingFor(typeof (T))); };

                    _parent._alterations.Add(alteration);
                }
            }

            public DocumentMappingExpression<T> GinIndexJsonData(Action<IndexDefinition> configureIndex = null)
            {
                alter = mapping =>
                {
                    var index = mapping.AddGinIndexToData();

                    configureIndex?.Invoke(index);
                };

                return this;
            }
        }
    }
}