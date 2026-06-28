using Orient.CrpcPlugin;
using Google.Protobuf.Compiler;

namespace Orient.Tests;

public class CRpcGeneratorTests : OrientTestBase
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
    public void GeneratedServiceBaseDoesNotImplementHttpJsonCodec()
    {
        var response = GenerateHelloWorld(includePush: true);

        var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
        Assert.Contains("public abstract class GreeterServiceBase : IRpcService", serverFile.Content);
        Assert.DoesNotContain("IRpcHttpJsonCodec", serverFile.Content);
        Assert.DoesNotContain("TryGetHttpMethodParsers", serverFile.Content);
    }

    [Fact]
    public void GeneratesPushHelperAndClientHandlerForServerPushMethod()
    {
        var response = GenerateHelloWorld(includePush: true);

        var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
        var clientFile = Assert.Single(response.File, file => file.Name.EndsWith("Client.cs"));
        Assert.Contains("protected OrientTask<bool> PushServerNoticeAsync", serverFile.Content);
        Assert.Contains("connection.SendPushAsync(1000, 2", serverFile.Content);
        Assert.Contains("protected virtual OrientTask OnPushServerNoticeAsync", clientFile.Content);
        Assert.Contains("RegisterPushHandler(", clientFile.Content);
        Assert.DoesNotContain("CallAsync(1000, 2", clientFile.Content);
    }

    [Fact]
    public void GeneratedServiceBaseReturnsMethodNotFoundForUnknownMethod()
    {
        var response = GenerateHelloWorld();

        var serverFile = Assert.Single(response.File, file => file.Name.EndsWith("Service.cs"));
        Assert.Contains("(int)CRpcStatusCode.MethodNotFound", serverFile.Content);
    }

    private static CodeGeneratorResponse GenerateHelloWorld(bool includePush = false)
    {
        var request = OrientCrpcGenTestFixtures.BuildHelloWorldRequest(includePush);
        var response = new CodeGeneratorResponse();
        OrientCrpcGen.Generate(request, response);
        Assert.True(string.IsNullOrEmpty(response.Error), response.Error);
        return response;
    }
}
