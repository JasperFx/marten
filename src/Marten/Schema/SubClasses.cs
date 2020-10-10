using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using BaselineTypeDiscovery;

namespace Marten.Schema
{
    public class SubClasses : IEnumerable<SubClassMapping>
    {
        private readonly DocumentMapping _parent;
        private readonly StoreOptions _options;
        private readonly IList<SubClassMapping> _subClasses = new List<SubClassMapping>();

        public SubClasses(DocumentMapping parent, StoreOptions options)
        {
            _parent = parent;
            _options = options;
        }

        public IEnumerator<SubClassMapping> GetEnumerator()
        {
            return _subClasses.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(Type subclassType, IEnumerable<MappedType> otherSubclassTypes, string alias)
        {
            VerifyIsSubclass(subclassType);

            var subclass = new SubClassMapping(subclassType, _parent, _options, otherSubclassTypes, alias);
            _subClasses.Add(subclass);
        }

        public void Add(Type subclassType, string alias = null)
        {
            VerifyIsSubclass(subclassType);

            var subclass = new SubClassMapping(subclassType, _parent, _options, alias);
            _subClasses.Add(subclass);
        }

        private void VerifyIsSubclass(Type subclassType)
        {
            if (!subclassType.CanBeCastTo(_parent.DocumentType))
                throw new ArgumentOutOfRangeException(nameof(subclassType),
                    $"Type '{subclassType.GetFullName()}' cannot be cast to '{_parent.DocumentType.GetFullName()}'");
        }


        /// <summary>
        ///     Programmatically directs Marten to map all the subclasses of <cref name="T" /> to a hierarchy of types.
        ///     <c>Unadvised in projects with many types.</c>
        /// </summary>
        /// <returns></returns>
        public void AddHierarchy()
        {
            var baseType = _parent.DocumentType;


            var assembly = baseType.GetTypeInfo().Assembly;

            var types = TypeRepository.ForAssembly(assembly).GetAwaiter().GetResult();
            var allSubclassTypes = types.ClosedTypes.Concretes
                .Where(x => x.CanBeCastTo(baseType));

            foreach (var subclassType in allSubclassTypes)
            {
                Add(subclassType, null);
            }

        }


        /// <summary>
        ///     Programmatically directs Marten to map all the subclasses of <cref name="T" /> to a hierarchy of types
        /// </summary>
        /// <param name="allSubclassTypes">
        ///     All the subclass types of <cref name="T" /> that you wish to map.
        ///     You can use either params of <see cref="Type" /> or <see cref="MappedType" /> or a mix, since Type can implicitly
        ///     convert to MappedType (without an alias)
        /// </param>
        /// <returns></returns>
        public void AddHierarchy(params MappedType[] allSubclassTypes)
        {
            allSubclassTypes.Each(subclassType =>
                Add(
                    subclassType.Type,
                    allSubclassTypes.Except(new[] {subclassType}),
                    subclassType.Alias
                )
            );
        }


    }
}
