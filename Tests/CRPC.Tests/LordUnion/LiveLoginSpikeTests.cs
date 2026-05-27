using LordUnion.IntegrationTests.Protocol;

namespace CRPC.Tests.LordUnion;

public class LiveLoginSpikeTests
{
    [Fact]
    public async Task LiveLoginSpike_ConnectsAndAttemptsPasswordLogin()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LORDUNION_LIVE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        uint appId = LoginWireConstants.DefaultAppId;
        var appIdText = Environment.GetEnvironmentVariable("LORDUNION_APP_ID");
        if (!string.IsNullOrWhiteSpace(appIdText))
        {
            Assert.True(uint.TryParse(appIdText, out appId), "LORDUNION_APP_ID must be a uint when set.");
        }

        var spike = new LoginWireSpike();
        var result = await spike.RunAsync(new LoginWireSpikeOptions
        {
            Host = Environment.GetEnvironmentVariable("LORDUNION_HOST") ?? "115.182.5.66",
            Port = int.TryParse(Environment.GetEnvironmentVariable("LORDUNION_PORT"), out var port) ? port : 30301,
            Username = Environment.GetEnvironmentVariable("LORDUNION_USERNAME") ?? "TJJ006628",
            Password = Environment.GetEnvironmentVariable("LORDUNION_PASSWORD") ?? "3YXRQW",
            AppId = appId,
        });

        Assert.True(
            result.Success,
            $"Live login spike failed. error={result.LoginErrorCode}, minimalParam={result.MinimalLoginAckParam}, minimalUserId={result.MinimalUserInfoUserId}, route(anon/login)={result.AnonymousRouteId}/{result.LoginRouteId}, aesKey={result.AesKey}, u64ServerTime={result.AnonymousU64ServerTime}, timestampMillis={result.ServerTimeMillisUsed}, userInfoUserId={result.UserInfoUserId}, ackMsgType={result.CommonLoginAckMsgType}, ackJson={result.DecryptedLoginAckJson}, message={result.Message}");
        Assert.True(result.UserId > 0);
    }
}
