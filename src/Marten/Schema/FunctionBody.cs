namespace Marten.Schema
{
    public class FunctionBody
    {
        public FunctionName Function { get; set; }
        public string[] DropStatements { get; set; }
        public string Body { get; set; }

        public FunctionBody(FunctionName function, string[] dropStatements, string body)
        {
            Function = function;
            DropStatements = dropStatements;
            Body = body;
        }
    }
}