using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Marten.Linq.Fields
{
    public interface IField
    {
        MemberInfo[] Members { get; }

        /// <summary>
        /// Postgresql locator that also casts the raw string data to the proper Postgresql type
        /// </summary>
        string TypedLocator { get; }

        /// <summary>
        /// Postgresql locator that returns the raw string value within the JSONB document
        /// </summary>
        string RawLocator { get; }

        /// <summary>
        /// May "correct" the raw value as appropriate for the constant parameter value
        /// within a compiled query
        /// </summary>
        /// <param name="valueExpression"></param>
        /// <returns></returns>
        object GetValueForCompiledQueryParameter(Expression valueExpression);

        /// <summary>
        /// The .Net type of this IField
        /// </summary>
        Type FieldType { get; }

        /// <summary>
        /// Locate the data for this field as JSONB
        /// </summary>
        string JSONBLocator { get; }

        string LocatorFor(string rootTableAlias);
        bool ShouldUseContainmentOperator();

        string SelectorForDuplication(string pgType);
    }
}
