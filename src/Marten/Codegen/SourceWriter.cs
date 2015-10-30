using System;
using System.Collections.Generic;
using System.IO;
using FubuCore;
using StoryTeller.Grammars;

namespace Marten.Codegen
{
    public class SourceWriter
    {
        private readonly StringWriter _writer = new StringWriter();

        private int _level = 0;
        private string _leadingSpaces = "";

        public int IndentionLevel
        {
            get { return _level; }
            set
            {
                _level = value;
                _leadingSpaces = "".PadRight(_level*4);
            }
        }

        public void WriteLine(string text)
        {
            _writer.WriteLine(_leadingSpaces + text);
        }

        public void BlankLine()
        {
            _writer.WriteLine();
        }

        public void Write(string text = null)
        {
            if (text.IsEmpty())
            {
                BlankLine();
                return;
            }

            text.ReadLines(line =>
            {
                line = line.Replace('`', '"');

                if (line.IsEmpty())
                {
                    BlankLine();
                }
                else if (line.StartsWith("BLOCK:"))
                {
                    WriteLine(line.Substring(6));
                    StartBlock();
                }
                else if (line.StartsWith("END"))
                {
                    FinishBlock();
                }
                else
                {
                    WriteLine(line);
                }
                
            });


        }

        public void StartNamespace(string @namespace)
        {
            WriteLine($"namespace {@namespace}");
            StartBlock();
        }

        private void StartBlock()
        {

            WriteLine("{");
            IndentionLevel++;
        }

        public void FinishBlock()
        {
            IndentionLevel--;
            WriteLine("}");

            BlankLine();
        }

        public IDisposable WriteMethod(MethodDef method)
        {
            method.WriteDeclaration(this);
            return InBlock();
        }

        public IDisposable InBlock(string declaration = null)
        {
            if (declaration.IsNotEmpty())
            {
                WriteLine(declaration);
            }
            StartBlock();
            return new BlockMarker(this);
        }

        public IDisposable StartClass(string declaration)
        {
            WriteLine(declaration);
            return InBlock();
        }

        public string Code()
        {
            return _writer.ToString();
        }
    }

    internal class BlockMarker : IDisposable
    {
        private readonly SourceWriter _parent;

        public BlockMarker(SourceWriter parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            _parent.FinishBlock();
        }
    }



    public class MethodDef
    {
        public string Name { get; private set; }
        public MemberAccess Access = MemberAccess.Public;
        public readonly IList<ArgDef> Args = new List<ArgDef>(); 

        public Type ReturnType;
        public string ReturnTypeName;

        public MethodDef(string name)
        {
            Name = name;
        }

        public MethodDef Returns<T>()
        {
            ReturnType = typeof (T);
            return this;
        }

        public MethodDef Returns(string typeName)
        {
            ReturnTypeName = typeName;
            return this;
        }

        public MethodDef WithArg<T>(string name)
        {
            var arg = new ArgDef(name) {Type = typeof(T)};
            Args.Add(arg);
            return this;
        }

        public MethodDef WithArg(string name, string typeName)
        {
            var arg = new ArgDef(name) {TypeName = typeName};
            Args.Add(arg);

            return this;
        }

        public void WriteDeclaration(SourceWriter sourceWriter)
        {
            throw new NotImplementedException();
        }
    }

    public enum MemberAccess
    {
        Public,
        Private,
        Internal
    }

    public class ArgDef
    {
        public Type Type { get; set; }
        public string TypeName { get; set; }
        public string Name { get; set; }

        public ArgDef(string name)
        {
            Name = name;
        }
    }
    
}