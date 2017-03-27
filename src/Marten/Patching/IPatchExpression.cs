using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Marten.Patching
{
    public interface IPatchExpression<T>
    {
        /// <summary>
        /// Set a single field or property value within the persisted JSON data
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="name"></param>
        /// <param name="value"></param>
        void Set<TValue>(string name, TValue value);

        /// <summary>
        /// Set a single field or property value within the persisted JSON data
        /// </summary>
        /// <typeparam name="TParent"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="name"></param>
        /// <param name="expression">Path to the parent location</param>
        /// <param name="value"></param>
        void Set<TParent, TValue>(string name, Expression<Func<T, TParent>> expression, TValue value);

        /// <summary>
        /// Set a single field or property value within the persisted JSON data
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="expression"></param>
        /// <param name="value"></param>
        void Set<TValue>(Expression<Func<T, TValue>> expression, TValue value);

        /// <summary>
        /// Copy a single field or property value within the persisted JSON data to one or more destinations
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="expression"></param>
        /// <param name="destinations"></param>
        void Duplicate<TElement>(Expression<Func<T, TElement>> expression, params Expression<Func<T, TElement>>[] destinations);

        /// <summary>
        /// Increment a single field or property by adding the increment value
        /// to the persisted value
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="increment"></param>
        void Increment(Expression<Func<T, int>> expression, int increment = 1);

        /// <summary>
        /// Increment a single field or property by adding the increment value
        /// to the persisted value
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="increment"></param>
        void Increment(Expression<Func<T, long>> expression, long increment = 1);

        /// <summary>
        /// Increment a single field or property by adding the increment value
        /// to the persisted value
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="increment"></param>
        void Increment(Expression<Func<T, double>> expression, double increment = 1);

        /// <summary>
        /// Increment a single field or property by adding the increment value
        /// to the persisted value
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="increment"></param>
        void Increment(Expression<Func<T, float>> expression, float increment = 1);

        /// <summary>
        /// Append an element to the end of a child collection on the persisted
        /// document
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="expression"></param>
        /// <param name="element"></param>
        void Append<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element);

        /// <summary>
        /// Append an element to the end of a child collection on the persisted
        /// document if the element does not already exist
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="expression"></param>
        /// <param name="element"></param>
        void AppendIfNotExists<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element);

        /// <summary>
        /// Insert an element at the designated index to a child collection on the persisted document
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="expression"></param>
        /// <param name="element"></param>
        /// <param name="index"></param>
        void Insert<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, int index = 0);

        /// <summary>
        /// Insert an element at the designated index to a child collection on the persisted document 
        /// if the value does not already exist at that index
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="expression"></param>
        /// <param name="element"></param>
        /// <param name="index"></param>
        void InsertIfNotExists<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, int index = 0);

        /// <summary>
        /// Remove element from a child collection on the persisted document
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="expression"></param>
        /// <param name="element"></param>
        /// <param name="action"></param>
        void Remove<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, RemoveAction action = RemoveAction.RemoveFirst);

        /// <summary>
        /// Rename a property or field in the persisted JSON document
        /// </summary>
        /// <param name="oldName"></param>
        /// <param name="expression"></param>
        void Rename(string oldName, Expression<Func<T, object>> expression);

        /// <summary>
        /// Delete a removed property or field in the persisted JSON data
        /// </summary>
        /// <param name="name">Redundant property or field name</param>
        void Delete(string name);

        /// <summary>
        /// Delete a removed property or field in the persisted JSON data
        /// </summary>
        /// <typeparam name="TParent"></typeparam>
        /// <param name="name">Redundant property or field name</param>
        /// <param name="expression">Path to the parent location</param>
        void Delete<TParent>(string name, Expression<Func<T, TParent>> expression);

        /// <summary>
        /// Delete an existing property or field in the persisted JSON data
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="expression">Path to the property or field to delete</param>
        void Delete<TElement>(Expression<Func<T, TElement>> expression);
    }
}