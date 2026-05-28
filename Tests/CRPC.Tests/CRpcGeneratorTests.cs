extern alias CRPC_PLUGIN;
using System.IO;
using CRPC_PLUGIN::CRpcProtobufPlugin;
using CodeGeneratorRequest = CRPC_PLUGIN::Google.Protobuf.Compiler.CodeGeneratorRequest;
using CodeGeneratorResponse = CRPC_PLUGIN::Google.Protobuf.Compiler.CodeGeneratorResponse;
using CodedOutputStream = CRPC_PLUGIN::Google.Protobuf.CodedOutputStream;
using FileDescriptorProto = CRPC_PLUGIN::Google.Protobuf.Reflection.FileDescriptorProto;
using FileOptions = CRPC_PLUGIN::Google.Protobuf.Reflection.FileOptions;
using MethodDescriptorProto = CRPC_PLUGIN::Google.Protobuf.Reflection.MethodDescriptorProto;
using MethodOptions = CRPC_PLUGIN::Google.Protobuf.Reflection.MethodOptions;
using ServiceDescriptorProto = CRPC_PLUGIN::Google.Protobuf.Reflection.ServiceDescriptorProto;
using ServiceOptions = CRPC_PLUGIN::Google.Protobuf.Reflection.ServiceOptions;
using WireFormat = CRPC_PLUGIN::Google.Protobuf.WireFormat;

namespace CRPC.Tests;

public class CRpcGeneratorTests : CrpcTestBase
{
    [Fact]
    public void GeneratesServiceBaseAndClientBaseNames()
    {
        var response = GenerateHelloWorld();

        var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
        var clientFile = Assert.Single(response.File, file => file.Name.EndsWith("Client.cs"));
        Assert.Contains("public abstract class GreeterServiceBase", serverFile.Content);
        Assert.Contains("public abstract class GreeterClientBase", clientFile.Content);
        Assert.DoesNotContain("public abstract class GreeterBase", serverFile.Content);
        Assert.DoesNotContain("public sealed class GreeterClient", clientFile.Content);
    }

    [Fact]
    public void GeneratesPushHelperAndClientHandlerForServerPushMethod()
    {
        var response = GenerateHelloWorld(includePush: true);

        var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
        var clientFile = Assert.Single(response.File, file => file.Name.EndsWith("Client.cs"));
        Assert.Contains("protected CRpcTask<bool> PushServerNoticeAsync", serverFile.Content);
        Assert.Contains("connection.SendPushAsync(1000, 2", serverFile.Content);
        Assert.Contains("protected virtual CRpcTask OnPushServerNoticeAsync", clientFile.Content);
        Assert.Contains("RegisterPushHandler(", clientFile.Content);
        Assert.DoesNotContain("CallAsync(1000, 2", clientFile.Content);
    }

    private static CodeGeneratorResponse GenerateHelloWorld(bool includePush = false)
    {
        var request = new CodeGeneratorRequest();
        request.ProtoFile.Add(BuildGreeterProto(includePush));
        var response = new CodeGeneratorResponse();
        CRpcGen.Generate(request, response);
        Assert.True(string.IsNullOrEmpty(response.Error), response.Error);
        return response;
    }

    private static FileDescriptorProto BuildGreeterProto(bool includePush)
    {
        var file = new FileDescriptorProto
        {
            Name = "helloworld.proto",
            Package = "example",
            Syntax = "proto3",
        };
        file.Options = new FileOptions { CsharpNamespace = "Example" };

        var greeter = new ServiceDescriptorProto { Name = "Greeter" };
        greeter.Options = CreateServiceOptions(serviceId: 1000);

        greeter.Method.Add(new MethodDescriptorProto
        {
            Name = "SayHello",
            InputType = ".example.HelloRequest",
            OutputType = ".example.HelloReply",
            Options = CreateMethodOptions(methodId: 1),
        });

        if (includePush)
        {
            greeter.Method.Add(new MethodDescriptorProto
            {
                Name = "ServerNotice",
                InputType = ".example.ServerNoticePush",
                OutputType = ".google.protobuf.Empty",
                Options = CreateMethodOptions(methodId: 2, serverPush: true),
            });
        }

        file.Service.Add(greeter);
        return file;
    }

    private static ServiceOptions CreateServiceOptions(int serviceId)
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream))
        {
            output.WriteTag(CRpcOptions.ServiceId, WireFormat.WireType.Varint);
            output.WriteInt32(serviceId);
        }

        return ServiceOptions.Parser.ParseFrom(stream.ToArray());
    }

    private static MethodOptions CreateMethodOptions(int methodId, bool serverPush = false)
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream))
        {
            output.WriteTag(CRpcOptions.MethodId, WireFormat.WireType.Varint);
            output.WriteInt32(methodId);
            if (serverPush)
            {
                output.WriteTag(CRpcOptions.ServerPush, WireFormat.WireType.Varint);
                output.WriteBool(true);
            }
        }

        return MethodOptions.Parser.ParseFrom(stream.ToArray());
    }
}
