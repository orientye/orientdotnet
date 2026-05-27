using System.Reflection;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Protocol;

public static class AckBodyDescriber
{
    public static string DescribeUnidentified(TKMobileAckMsg ack)
    {
        ArgumentNullException.ThrowIfNull(ack);

        return
            $"lobby={DescribeContainer(ack.LobbyAckMsg)} match={DescribeContainer(ack.MatchAckMsg)} lord={DescribeContainer(ack.LordAckMsg)}";
    }

    private static string DescribeContainer(object? container)
    {
        if (container is null)
        {
            return "(empty)";
        }

        var populated = container.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => IsPopulated(property, container))
            .Select(property => FormatPropertyName(property.Name))
            .ToList();

        return populated.Count == 0 ? "(empty)" : string.Join("+", populated);
    }

    private static bool IsPopulated(PropertyInfo property, object container)
    {
        var value = property.GetValue(container);
        if (value is null)
        {
            return false;
        }

        return property.PropertyType != typeof(string) || !string.IsNullOrEmpty((string)value);
    }

    private static string FormatPropertyName(string propertyName)
    {
        return propertyName.EndsWith("AckMsg", StringComparison.Ordinal)
            ? propertyName[..^6]
            : propertyName;
    }
}
