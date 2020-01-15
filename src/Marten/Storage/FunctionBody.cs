using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Storage
{
    public class FunctionBody
    {
        public DbObjectName Function { get; set; }
        public string[] DropStatements { get; set; }
        public string Body { get; set; }

        public string Signature()
        {
            var functionIndex = Body.IndexOf("FUNCTION", StringComparison.OrdinalIgnoreCase);
            var openParen = Body.IndexOf("(");
            var closeParen = Body.IndexOf(")");

            var args = Body.Substring(openParen + 1, closeParen - openParen - 1).Trim()
                .Split(',').Select(x =>
                {
                    var parts = x.Trim().Split(' ');
                    return parts.Skip(1).Join(" ");
                }).Join(", ");

            var nameStart = functionIndex + "function".Length;
            var funcName = Body.Substring(nameStart, openParen - nameStart).Trim();

            return $"{funcName}({args})";
        }

        public string ToOwnershipCommand(string owner)
        {
            return $"ALTER FUNCTION {Signature()} OWNER TO \"{owner}\";";
        }

        public FunctionBody(DbObjectName function, string[] dropStatements, string body)
        {
            Function = function;
            DropStatements = dropStatements;
            Body = body;
        }

        public string BuildTemplate(string template)
        {
            return template
                .Replace(DdlRules.SCHEMA, Function.Schema)
                .Replace(DdlRules.FUNCTION, Function.Name)
                .Replace(DdlRules.SIGNATURE, Signature())
                ;
        }

        public void WriteTemplate(DdlTemplate template, StringWriter writer)
        {
            var text = template?.FunctionCreation;
            if (text.IsNotEmpty())
            {
                writer.WriteLine(BuildTemplate(text));
            }
        }
    }
}
