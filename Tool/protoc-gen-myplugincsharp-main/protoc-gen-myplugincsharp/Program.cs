using System.Text;
using CRpcOptions;
using Google.Protobuf;
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;

namespace protoc_gen_myplugincsharp;
// assume current directory is the output directory, and it contains protoc.exe.
// protoc.exe --plugin=protoc-gen-myplugincsharp.exe --myplugincsharp_out=./ --proto_path=%userprofile%\.nuget\packages\google.protobuf.tools\3.21.1\tools --proto_path=./ chat.proto

internal class Program
{
    public static void ParseCode(CodeGeneratorRequest request, CodeGeneratorResponse response)
    {
        //DotbpeGen.Generate(request,response);
        Generate(request, response);
    }

    public static void Generate(CodeGeneratorRequest request, CodeGeneratorResponse response)
    {
        foreach (var protofile in request.ProtoFile)
            try
            {
                GenerateByProtoFile(protofile, response);
            }
            catch (Exception ex)
            {
                using (Stream stream = File.Create("./error.txt"))
                {
                    var err = Encoding.UTF8.GetBytes(ex.Message + ex.StackTrace);
                    stream.Write(err, 0, err.Length);
                }

                response.Error += ex.Message;
            }
    }

    private static void GenerateByProtoFile(FileDescriptorProto protofile, CodeGeneratorResponse response)
    {
        GenerateServer(protofile, response);
    }

    private static void GenerateServer(FileDescriptorProto protofile, CodeGeneratorResponse response)
    {
        var sb = new StringBuilder();
        //生成代码
        foreach (var t in protofile.Service) GenerateServiceForServer(t, sb);

        var nfile = new CodeGeneratorResponse.Types.File
        {
            Name = protofile.Name,
            Content = sb.ToString()
        };
        response.File.Add(nfile);
    }

    private static void GenerateServiceForServer(ServiceDescriptorProto service, StringBuilder sb)
    {
        //int serviceId;
        var serviceId = service.Options.GetExtension(CrpcOptionsExtensions.ServiceId);
        var has = service.Options.HasExtension(CrpcOptionsExtensions.ServiceId);

        var serviceOptions = service.Options;
        if (serviceId <= 0)
            throw new Exception(
                $"****** Service={service.Name} ServiceId NOT_FOUND  has={has}, t={serviceOptions.GetType()},  s={serviceOptions}");

        throw new Exception(
            $"Service={service.Name} ServiceId SUCCESS  has={has}, t={serviceOptions.GetType()},  s={serviceOptions}");
    }

    private static void Main(string[] args)
    {
        var tm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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

    //     // you can attach debugger
    //     // System.Diagnostics.Debugger.Launch();
    //
    //     // get request from standard input
    //     CodeGeneratorRequest request;
    //     using (var stdin = Console.OpenStandardInput())
    //     {
    //         request = Deserialize<CodeGeneratorRequest>(stdin);
    //     }
    //
    //     var response = new CodeGeneratorResponse();
    //     
    //     foreach (var file in request.ProtoFile)
    //     {
    //         var output = new StringBuilder();
    //         
    //         output.AppendLine($"this proto file name: {file.Name}, {tm}");
    //
    //         // make service method list
    //         foreach (var serviceDescriptorProto in file.Service)
    //         {
    //             output.AppendLine($"\nservice {serviceDescriptorProto.Name}");
    //
    //             foreach (var methodDescriptorProto in serviceDescriptorProto.Method)
    //             {
    //                 output.AppendLine($"   method:    {methodDescriptorProto.OutputType} {methodDescriptorProto.Name}({methodDescriptorProto.InputType})");
    //                 var messageId = methodDescriptorProto.Options.GetExtension(Fightoptionsh5Extensions.MessageId);
    //                 output.AppendLine($"   method id:    {messageId}");
    //             }
    //
    //             var serviceOptions = serviceDescriptorProto.Options;
    //             output.AppendLine($"   service options string:    {serviceOptions.ToString()}");
    //             output.AppendLine($"   service options type:    {serviceOptions.GetType()}");
    //             var serviceId = serviceOptions.GetExtension(Fightoptionsh5Extensions.ServiceId);
    //             output.AppendLine($"   service id:    {serviceId}");
    //         }
    //
    //         // make message field list
    //         foreach (var descriptorProto in file.MessageType)
    //         {
    //             output.AppendLine($"\nmessage {descriptorProto.Name}");
    //
    //             foreach (var field in descriptorProto.Field)
    //             {
    //                 output.AppendLine($"   field:    ty-{field.TypeName}    name-{field.Name}");
    //             }
    //         }
    //
    //         // set as response
    //         response.File.Add(
    //             new CodeGeneratorResponse.Types.File()
    //             {
    //                 Name = file.Name + ".txt",
    //                 Content = output.ToString(),
    //             }
    //         );
    //     }
    //
    //     // set result to standard output
    //     using (var stdout = Console.OpenStandardOutput())
    //     {
    //         response.WriteTo(stdout);
    //     }
    // }
    //
    // static T Deserialize<T>(Stream stream) where T : IMessage<T>, new()
    //     => new MessageParser<T>(() => new T()).ParseFrom(stream);
}