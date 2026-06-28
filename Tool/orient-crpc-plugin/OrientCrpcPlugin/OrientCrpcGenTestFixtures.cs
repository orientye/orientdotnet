using CRpcOptions;
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;

namespace Orient.CrpcPlugin;

public static class OrientCrpcGenTestFixtures
{
    public static CodeGeneratorRequest BuildHelloWorldRequest(bool includePush = false)
    {
        var request = new CodeGeneratorRequest();
        request.ProtoFile.Add(BuildGreeterProto(includePush));
        return request;
    }

    private static FileDescriptorProto BuildGreeterProto(bool includePush)
    {
        var file = new FileDescriptorProto
        {
            Name = "helloworld.proto",
            Package = "example",
            Syntax = "proto3",
        };
        file.Options = new Google.Protobuf.Reflection.FileOptions { CsharpNamespace = "Example" };

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
        var options = new ServiceOptions();
        options.SetExtension(CrpcOptionsExtensions.ServiceId, serviceId);
        return options;
    }

    private static MethodOptions CreateMethodOptions(int methodId, bool serverPush = false)
    {
        var options = new MethodOptions();
        options.SetExtension(CrpcOptionsExtensions.MethodId, methodId);
        if (serverPush)
        {
            options.SetExtension(CrpcOptionsExtensions.ServerPush, true);
        }

        return options;
    }
}
