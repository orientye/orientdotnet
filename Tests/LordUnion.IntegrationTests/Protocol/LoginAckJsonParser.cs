using Newtonsoft.Json.Linq;

namespace LordUnion.IntegrationTests.Protocol;

public static class LoginAckJsonParser
{
    public static uint? TryGetUserId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var token = JObject.Parse(json)["pid"];
            return token?.Type switch
            {
                JTokenType.Integer => token.Value<uint>(),
                JTokenType.Float => (uint)token.Value<double>(),
                JTokenType.String when uint.TryParse(token.Value<string>(), out var parsed) => parsed,
                _ => null,
            };
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    public static ulong? TryGetSessionId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var token = JObject.Parse(json)["G_SessionID"];
            return token?.Type switch
            {
                JTokenType.Integer => token.Value<ulong>(),
                JTokenType.Float => (ulong)token.Value<double>(),
                JTokenType.String when ulong.TryParse(token.Value<string>(), out var parsed) => parsed,
                _ => null,
            };
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }
}