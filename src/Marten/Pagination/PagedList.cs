using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Marten.Pagination
{
    public class PagedList<T> : IEnumerable<T>
    {
        private readonly List<T> _subset = new List<T>();
        private PagedListMetaData _metaData;

        public PagedList(int pageNumber, int pageSize, int totalItemCount, IEnumerable<T> items)
        {
            this._metaData = new PagedListMetaData(pageNumber, pageSize, totalItemCount);

            if (items != null)
                this._subset.AddRange(items);
        }

        public T this[int index]
        {
            get { return this._subset[index]; }
        }
        public int Count
        {
            get { return this._subset.Count(); }
        }

        public PagedListMetaData MetaData
        {
            get { return this._metaData;  }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)this._subset).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<T>)this._subset).GetEnumerator();
        }
    }
}