using System;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;

namespace Marten.Schema
{
    public static class DocumentStorageBuilder
    {
        public static IDocumentStorage Build(Type documentType)
        {
            var prop =
                documentType.GetProperties().Where(x => StringExtensions.EqualsIgnoreCase(x.Name, "id") && x.CanWrite).FirstOrDefault();

            if (prop == null) throw new ArgumentOutOfRangeException("documentType", "Type {0} does not have a public settable property named 'id' or 'Id'".ToFormat(documentType.FullName));

            var parameter = Expression.Parameter(documentType, "x");
            var propExpression = Expression.Property(parameter, prop);

            var lambda = Expression.Lambda(propExpression, parameter);

            var func = lambda.Compile();

            return typeof (DocumentStorage<,>).CloseAndBuildAs<IDocumentStorage>(func, documentType, prop.PropertyType);
        }


    }
}