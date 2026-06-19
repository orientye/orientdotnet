using CRpcProtobufPlugin;
using Google.Protobuf.Compiler;

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
        var request = CRpcGenTestFixtures.BuildHelloWorldRequest(includePush);
        var response = new CodeGeneratorResponse();
        CRpcGen.Generate(request, response);
        Assert.True(string.IsNullOrEmpty(response.Error), response.Error);
        return response;
    }
}
