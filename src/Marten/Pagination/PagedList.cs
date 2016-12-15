using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Pagination
{
    /// <summary>
    /// Class to return The paged list from a paged query.
    /// </summary>
    /// <typeparam name="T">Document Type</typeparam>
    public class PagedList<T> : IEnumerable<T>
    {
        /// <summary>
        /// Field to hold the paged query result
        /// </summary>
        private readonly List<T> _subset = new List<T>();

        /// <summary>
        /// Field to hold page meta data
        /// </summary>
        private PagedListMetaData _metaData;

        /// <summary>
        /// Initializes a new instance of the <see cref="PagedList{T}" /> class.
        /// </summary>
        /// <param name="pageNumber">Current page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="totalItemCount">Total count of all records</param>
        /// <param name="items">Paged query result</param>
        public PagedList(int pageNumber, int pageSize, int totalItemCount, IEnumerable<T> items)
        {
            this._metaData = new PagedListMetaData(pageNumber, pageSize, totalItemCount);

            if (items != null)
            {
                this._subset.AddRange(items);
            }
        }

        /// <summary>
        /// Return the paged query result
        /// </summary>
        /// <param name="index">Index to fetch item from paged query result</param>
        /// <returns>/returns item from paged query result</returns>
        public T this[int index]
        {
            get { return this._subset[index]; }
        }

        /// <summary>
        /// Return the number of records in the paged query result
        /// </summary>
        public int Count
        {
            get { return this._subset.Count(); }   
        }

        /// <summary>
        /// Return page meta data
        /// </summary>
        public PagedListMetaData MetaData
        {
            get { return this._metaData; }
        }

        /// <summary>
        /// Generic Enumerator
        /// </summary>
        /// <returns>Generic Enumerator of paged query result</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)this._subset).GetEnumerator();
        }

        /// <summary>
        /// Enumerator
        /// </summary>
        /// <returns>Enumerator of paged query result</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<T>)this._subset).GetEnumerator();
        }
    }
}