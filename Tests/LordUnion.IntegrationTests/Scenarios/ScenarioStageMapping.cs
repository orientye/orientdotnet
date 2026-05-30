using System.Text.RegularExpressions;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Scenarios;

internal static class ScenarioStageMapping
{
    public static LoginStageResult FromLoginFailure(AccountSession session)
    {
        var loginErrorCode = (int)(session.LoginErrorCode ?? 0);
        return new LoginStageResult(
            loginErrorCode,
            session.UserId ?? 0,
            session.SessionId,
            session.Nickname,
            session.AesKey ?? string.Empty,
            $"Login failed with error code {loginErrorCode}.");
    }

    public static SignupStageResult FromSignupFailure(
        LordUnionGameProfile profile,
        InvalidOperationException ex)
    {
        var signupErrorCode = TryParseSignupErrorCode(ex.Message);
        return new SignupStageResult(
            (int)signupErrorCode,
            signupErrorCode,
            profile.TourneyId,
            profile.MatchPoint,
            profile.GameId,
            $"Tourney signup failed with error code {signupErrorCode}.");
    }

    private static uint TryParseSignupErrorCode(string message)
    {
        var signupAckMatch = Regex.Match(
            message,
            @"TourneySignupAck param=(\d+)",
            RegexOptions.CultureInvariant);
        if (signupAckMatch.Success
            && uint.TryParse(signupAckMatch.Groups[1].Value, out var signupAckCode)
            && signupAckCode != 0)
        {
            return signupAckCode;
        }

        var mobileMatch = Regex.Match(message, @"mobile\.param=(\d+)", RegexOptions.CultureInvariant);
        if (mobileMatch.Success && uint.TryParse(mobileMatch.Groups[1].Value, out var mobileCode) && mobileCode != 0)
        {
            return mobileCode;
        }

        var match = Regex.Match(message, @"param=(\d+)", RegexOptions.CultureInvariant);
        return match.Success && uint.TryParse(match.Groups[1].Value, out var errorCode)
            ? errorCode
            : 0;
    }
}
