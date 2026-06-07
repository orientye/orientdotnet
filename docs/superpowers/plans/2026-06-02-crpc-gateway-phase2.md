# CRpc Gateway Phase 2 Implementation Plan

**Goal:** `BackendPool` + round-robin across connections + JSON config + dual HelloWorld demo.

**Spec:** `docs/superpowers/specs/2026-06-02-crpc-gateway-phase2-design.md`

**Task order:** 1 → 2 → 3 → 4 → 5 → 6

---

## Task 1: BackendPool types + picker tests

- Create `BackendEndpoint.cs`, `BackendPool.cs`, `IBackendPicker.cs`, `RoundRobinPicker.cs`, `BackendPoolRegistry.cs`
- Tests: `BackendPoolTests.cs`

## Task 2: Config loader

- Create `GateWayConfig.cs`, `GateWayConfigLoader.cs`, `Example/GateWay/gateway.json`
- Deprecate single `BackendHost/Port` on `GateWayOptions` → merge into `GateWayConfig`

## Task 3: Wire pool into session table + connector + link

- `IBackendConnector.ConnectAsync(client, BackendEndpoint)`
- `GateWayBackendLink.Endpoint`
- `GetOrCreateAsync(inbound, serviceId, registry, config, loop)`

## Task 4: Health + forwarder marks unhealthy

- `GateWayServiceImpl` marks endpoint unhealthy on connect/RPC failure
- `MarkHealthy` on successful reconnect

## Task 5: Host + HelloWorld port arg

- `GateWayServer/Program.cs` loads config
- HelloWorld `Program.cs` `--port`

## Task 6: Verify

- `dotnet test` + update Phase 1 tests
- Manual checklist in spec
