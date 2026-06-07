using System.Text.Json;
using System.Text.Json.Serialization;

namespace GateWay;

public static class GateWayConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static GateWayConfig LoadOrDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Console.WriteLine("GateWay: config file not found, using in-memory demo pool (7999 + 8001).");
            return GateWayConfig.CreateDefaultDemo();
        }

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<GateWayConfigDto>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse gateway config: {path}");

        return dto.ToConfig();
    }

    private sealed class GateWayConfigDto
    {
        [JsonPropertyName("listenPort")]
        public int ListenPort { get; init; } = 7000;

        [JsonPropertyName("defaultTimeoutMs")]
        public int DefaultTimeoutMs { get; init; } = 5000;

        [JsonPropertyName("fallbackServiceId")]
        public ushort FallbackServiceId { get; init; }

        [JsonPropertyName("pools")]
        public List<GateWayPoolConfigDto> Pools { get; init; } = [];

        public GateWayConfig ToConfig()
        {
            return new GateWayConfig
            {
                ListenPort = ListenPort,
                DefaultTimeoutMs = DefaultTimeoutMs,
                FallbackServiceId = FallbackServiceId,
                Pools = Pools.Select(pool => pool.ToConfig()).ToArray(),
            };
        }
    }

    private sealed class GateWayPoolConfigDto
    {
        [JsonPropertyName("serviceId")]
        public ushort ServiceId { get; init; }

        [JsonPropertyName("pickPolicy")]
        public string PickPolicy { get; init; } = "roundRobin";

        [JsonPropertyName("endpoints")]
        public List<GateWayEndpointConfigDto> Endpoints { get; init; } = [];

        public GateWayPoolConfig ToConfig()
        {
            if (!string.Equals(PickPolicy, "roundRobin", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported pickPolicy '{PickPolicy}'. Only roundRobin is supported in Phase 2.");
            }

            return new GateWayPoolConfig
            {
                ServiceId = ServiceId,
                PickPolicy = PickPolicy,
                Endpoints = Endpoints.Select(endpoint => endpoint.ToConfig()).ToArray(),
            };
        }
    }

    private sealed class GateWayEndpointConfigDto
    {
        [JsonPropertyName("host")]
        public string Host { get; init; } = "127.0.0.1";

        [JsonPropertyName("port")]
        public int Port { get; init; }

        [JsonPropertyName("weight")]
        public int Weight { get; init; } = 1;

        public GateWayEndpointConfig ToConfig()
        {
            return new GateWayEndpointConfig
            {
                Host = Host,
                Port = Port,
                Weight = Weight,
            };
        }
    }
}
