using System.Text;
using Google.Protobuf;
using Google.Protobuf.Compiler;

namespace Orient.CrpcPlugin;

internal static class Program
{
    private static void Main()
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

            OrientCrpcGen.Generate(request, response);
        }
        catch (Exception e)
        {
            response.Error += e.ToString();
        }

        using (var output = Console.OpenStandardOutput())
        {
            var bytes = response.ToByteArray();
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }
    }
}
