using System;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Compiler;

namespace CRpcProtobufPlugin
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var response = new CodeGeneratorResponse();
            try
            {
                CodeGeneratorRequest request;
                using (var inStream = Console.OpenStandardInput())
                {
                    request = CodeGeneratorRequest.Parser.ParseFrom(inStream);
                }

                ParseCode(request, response);
            }
            catch (Exception e)
            {
                response.Error += e.ToString();
            }

            using (var output = Console.OpenStandardOutput())
            {
                response.WriteTo(output);
                output.Flush();
            }
        }

        private static void ParseCode(CodeGeneratorRequest request, CodeGeneratorResponse response)
        {
            CRpcGen.Generate(request, response);
        }
    }
}