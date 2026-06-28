# Orient.Tests / Tool Rename Design

**Status:** Approved (implemented 2026-06-28)  
**Date:** 2026-06-28  
**Prerequisite:** A2 + Runtime/Rpc split completed  
**Related:** `2026-06-28-orient-runtime-rpc-split-design.md`  
**Supersedes:** `2026-06-28-orient-zero-crpc-rename-design.md` (cancelled — protocol `CRpc*` names retained)

---

## Goal

Rename **test projects** and **protobuf Tool** to Orient branding. Keep all **protocol/runtime CRpc type names** (`CRpcServer`, `CRpcClient`, wire magic, `crpc://`, etc.) unchanged.

**Depth:** Standard (B) — projects, folders, namespaces, csproj/sln refs, scripts; **not** splitting test projects; **not** renaming test source filenames that reference CRpc protocol types.

---

## In Scope

### Test projects

| Before | After |
| --- | --- |
| `CRPC.TestHelper/` | `Orient.TestHelper/` |
| `CRPC.TestHelper.csproj` | `Orient.TestHelper.csproj` |
| namespace `CRpc.TestHelper` | `Orient.TestHelper` |
| `CrpcTestBase` | `OrientTestBase` |
| `Tests/CRPC.Tests/` | `Tests/Orient.Tests/` |
| `CRPC.Tests.csproj` | `Orient.Tests.csproj` |
| namespace `CRPC.Tests` | `Orient.Tests` |
| `CRPC.Tests.GateWayTests` | `Orient.Tests.GateWay` |

`InternalsVisibleTo("CRPC.Tests")` → `InternalsVisibleTo("Orient.Tests")` in `Orient.Runtime`, `Orient.Rpc`, `GateWay.Core`.

**Keep filenames:** `CRpcServerTests.cs`, `CRpcGeneratorTests.cs`, etc. (they test CRpc protocol types).

### CRpc protobuf Tool（`orient-crpc` 命名）

目录与项目用 **Orient + CRpc** 组合；protoc 插件二进制与协议 option **保留 crpc**（与 `CRpcServer` 等同层命名一致）。

| Before | After |
| --- | --- |
| `Tool/crpc-protobuf-plugin/` | `Tool/orient-crpc-plugin/` |
| `Tool/crpc-protobuf-plugin-tool/` | `Tool/orient-crpc-tool/` |
| `CRpcProtobufPlugin/` | `OrientCrpcPlugin/` |
| `CRpcProtobufPlugin.csproj` | `OrientCrpcPlugin.csproj` |
| namespace `CRpcProtobufPlugin` | `Orient.CrpcPlugin` |
| `CRpcGen.cs` | `OrientCrpcGen.cs` |
| `CRpcOptionsReader.cs` | `OrientCrpcOptionsReader.cs` |
| `CRpcGenTestFixtures.cs` | `OrientCrpcGenTestFixtures.cs` |
| `<AssemblyName>protoc-gen-crpc</AssemblyName>` | **不变** |
| protoc `--crpc_out` | **不变** |
| `Orient.Rpc/Codec/crpc-options.proto` | **不变**（`package crpc`，`(crpc.service_id)` 等） |
| `CRpcOptions.cs` / `csharp_namespace` | **不变**（`CRpcOptions`） |

`postbuild.bat` 复制目标 → `orient-crpc-tool/`。

Update `gen-helloworld.bat`：

- plugin 路径 → `../orient-crpc-plugin/OrientCrpcPlugin/...`
- `--plugin=protoc-gen-crpc.exe --crpc_out=...`（不变）
- proto include → `../../Orient.Rpc/Codec/`

Regenerate HelloWorld generated files after plugin rebuild（生成物仍引用 `ICRpcGeneratedClient` 等）。

### Solution & references

- `orient-dotnet.sln` project entries
- `Orient.Tests.csproj` → `Tool/orient-crpc-plugin/OrientCrpcPlugin/OrientCrpcPlugin.csproj`
- `postbuild.bat` copy target → `orient-crpc-tool`

---

## Out of Scope (explicitly keep)

| Item | Reason |
| --- | --- |
| `CRpcServer`, `CRpcClient`, `CRpcMessage`, … | Protocol API — stable names |
| `ICRpcGeneratedClient` | Generated client contract |
| Wire magic `0x43525043` | Binary protocol |
| URL scheme `crpc://` | Client reference URLs |
| `Doc/architecture-draft.md` CRpc terminology | Describes protocol layer |
| Historical `docs/superpowers/*crpc*` filenames | Archive; no sweep |
| Splitting `Orient.Tests` into Runtime/Rpc test projects | Future |

---

## Verification

1. `dotnet test orient-dotnet.sln` — all pass  
2. Rebuild plugin + run `Tool/orient-crpc-tool/gen-helloworld.bat`  
3. Grep confirms no stale paths: `crpc-protobuf-plugin`, `CRPC.Tests`, `CRPC.TestHelper`

---

## Implementation order

1. Tool folder move (`orient-crpc-plugin` / `orient-crpc-tool`) + code rename + rebuild  
2. Regenerate HelloWorld（proto options 未改，仅路径）  
3. TestHelper rename  
4. Orient.Tests rename + namespace sweep  
5. sln/csproj/InternalsVisibleTo  
6. Full test run

---

## Approval

- [x] User approved this spec (2026-06-28)
- [x] Implemented — see git diff; verify with `dotnet test orient-dotnet.sln`
