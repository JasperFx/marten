using System;
using System.Linq;
using Baseline;

namespace Marten.Schema
{
    public class FunctionBody
    {
        public FunctionName Function { get; set; }
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

        public FunctionBody(FunctionName function, string[] dropStatements, string body)
        {
            Function = function;
            DropStatements = dropStatements;
            Body = body;
        }
    }
}