# Orient Zero-CRPC Rename Design

**Status:** Cancelled — user reverted to partial rename scope; see `2026-06-28-orient-tests-tool-rename-design.md`  
**Date:** 2026-06-28  
**Prerequisite:** A2 + Runtime/Rpc split completed  
**Supersedes:** Nothing — **do not implement this document.**

---

## Goal

Eliminate **every** occurrence of `crpc` / `Crpc` / `CRpc` / `CRPC` from the repository:

- Source, projects, folders, filenames
- Generated code and proto options
- Scripts, solution entries, `InternalsVisibleTo`
- Active documentation (`Doc/`, current specs)
- Error messages, log strings, URL schemes
- Historical doc **filenames** under `docs/superpowers/` (content may stay historical but filenames must not contain `crpc`)

**Verification gate:** case-insensitive ripgrep for `crpc` over the repo (excluding `.git/`, `bin/`, `obj/`) must return **zero** matches after delivery.

---

## Non-Goals

- Splitting `Orient.Tests` into multiple test projects (future)
- Renaming `Orient.Runtime` / `Orient.Rpc` assembly names (already correct)
- External repos (`data-manager`, `center`) — out of scope

---

## Naming Convention

Drop the `CRpc` prefix on protocol types. Types live in existing namespaces (`Orient.Rpc.Server`, `Orient.Rpc.Client`, `Orient.Rpc.Codec`); class names use **`Rpc*`** (not `OrientRpc*` — namespace already provides brand).

| Before | After |
| --- | --- |
| `CRpcServer` | `RpcServer` |
| `CRpcClient` | `RpcClient` |
| `CRpcMessage` | `RpcMessage` |
| `CRpcMessageHeader` | `RpcMessageHeader` |
| `CRpcMessageType` | `RpcMessageType` |
| `CRpcMessageDecoder` / `Encoder` | `RpcMessageDecoder` / `RpcMessageEncoder` |
| `CRpcFrameFlags` | `RpcFrameFlags` |
| `CRpcContext` | `RpcContext` |
| `CRpcConnection` | `RpcConnection` |
| `CRpcConnectionRegistry` | `RpcConnectionRegistry` |
| `CRpcServerHandler` | `RpcServerHandler` |
| `CRpcServerOptions` | `RpcServerOptions` |
| `CRpcServerPipelineFactory` | `RpcServerPipelineFactory` |
| `CRpcServerReadIdleHandler` | `RpcServerReadIdleHandler` |
| `CRpcClientOptions` | `RpcClientOptions` |
| `CRpcClientPipelineFactory` | `RpcClientPipelineFactory` |
| `CRpcClientHeartbeatHandler` | `RpcClientHeartbeatHandler` |
| `CRpcLoopHost` | **Delete** — callers use `OrientLoopHost` |
| `CRpcClientLoopHost` | **Delete** — callers use `OrientLoopHost` |
| `CRpcStatusCode` | `RpcStatusCode` |
| `CRpcReference<T>` | `RpcReference<T>` |
| `CRpcReferenceBuilder<T>` | `RpcReferenceBuilder<T>` |
| `CRpcProxyActivator` | `RpcProxyActivator` |
| `CRpcPushHandler` / `Context` | `RpcPushHandler` / `RpcPushContext` |
| `ICRpcGeneratedClient` | `IRpcGeneratedClient` |
| `BuildCrpcResponse` | `BuildRpcResponse` |
| Namespace `Orient.Rpc.CRpc` | `Orient.Rpc.Protocol` |
| Folder `Orient.Rpc/CRpc/` | `Orient.Rpc/Protocol/` |

Generated client/server bases continue to implement `IRpcGeneratedClient`.

---

## Wire Protocol & URL Scheme

### Frame magic (breaking change)

| Before | After |
| --- | --- |
| `0x43525043` (`'CRPC'` LE) | `0x544E524F` (`'ORNT'` LE) |

- Update `RpcMessage.Magic`, decoder/encoder, `PortUnificationHandler` sniff logic, `Doc/protocol.md`.
- No backward compatibility shim — greenfield rename delivery.
- Comments and docs refer to **“frame magic”** + hex only; never spell the old ASCII label.

### Reference URL scheme

| Before | After |
| --- | --- |
| `crpc://host:port` | `orient://host:port` |

Update `RpcReferenceBuilder`, examples, tests, and any config samples.

---

## Protobuf Plugin (Tool)

| Before | After |
| --- | --- |
| `Tool/crpc-protobuf-plugin/` | `Tool/orient-protobuf-plugin/` |
| `Tool/crpc-protobuf-plugin-tool/` | `Tool/orient-protobuf-plugin-tool/` |
| `CRpcProtobufPlugin/` | `OrientProtobufPlugin/` |
| namespace `CRpcProtobufPlugin` | `Orient.ProtobufPlugin` |
| `CRpcGen` | `OrientGen` |
| `CRpcOptionsReader` | `OrientOptionsReader` |
| `CRpcGenTestFixtures` | `OrientGenTestFixtures` |
| `<AssemblyName>protoc-gen-crpc</AssemblyName>` | `protoc-gen-orient` |
| protoc flag `--crpc_out` | `--orient_out` |
| `Orient.Rpc/Codec/crpc-options.proto` | `orient-options.proto` |
| `package crpc` | `package orient` |
| `csharp_namespace = "CRpcOptions"` | `"Orient.Protobuf"` |
| `(crpc.service_id)` etc. | `(orient.service_id)` etc. |

Extension field numbers **60001–60003** unchanged.

Regenerate HelloWorld after rename; update `gen-helloworld.bat` proto paths to `Orient.Rpc/Codec/`.

---

## Test Projects

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

### Test filenames (rename all)

Every `CRpc*.cs` test file → `Rpc*.cs` (e.g. `CRpcServerTests.cs` → `RpcServerTests.cs`, `CRpcGeneratorTests.cs` → `OrientGeneratorTests.cs`).

`CRpcTestMessages.cs` → `RpcTestMessages.cs`.

`InternalsVisibleTo("CRPC.Tests")` → `InternalsVisibleTo("Orient.Tests")` in `Orient.Runtime`, `Orient.Rpc`, `GateWay.Core`.

---

## Examples & Gateway

Update all Example/GateWay references:

- Types: `RpcServer`, `RpcClient`, `RpcContext`, etc.
- `HelloworldClient.cs` / generated bases: `IRpcGeneratedClient`
- HTTP/unified handlers: magic sniff uses `RpcMessage.Magic`
- Client reference URLs: `orient://`

---

## Documentation

### Active docs (content rewrite)

| File | Action |
| --- | --- |
| `Doc/architecture-draft.md` | Full pass: title → “Orient 架构”; replace all CRpc names with Orient/Rpc names |
| `Doc/protocol.md` | Title → “Orient Binary Protocol”; ORNT magic; Rpc* handler names |

### Historical specs/plans (filename rename)

Rename 23 files under `docs/superpowers/` whose paths contain `crpc` → substitute `orient-rpc` or `orient` (e.g. `2026-06-19-crpc-binary-codec-design.md` → `2026-06-19-orient-binary-codec-design.md`). Update internal cross-links in `2026-06-28-*` specs.

Historical **body text** may still describe past decisions; filenames and new cross-references must not contain `crpc`.

### Split spec cleanup

Update `2026-06-28-orient-runtime-rpc-split-design.md` namespace table and non-goals to reflect this rename supersedes “keep CRpc* names”.

---

## Solution & Project References

- `orient-dotnet.sln`: project entries → `Orient.Tests`, `Orient.TestHelper`, `OrientProtobufPlugin`
- All `ProjectReference` paths updated
- Delete stale `Tool/crpc-protobuf-plugin-tool/protoc-gen-crpc.*` artifacts; rebuild produces `protoc-gen-orient.*`

---

## Implementation Order

1. **Orient.Rpc type rename** — move files, rename types, delete `RpcLoopHost` aliases, fix namespaces
2. **Wire magic + URL scheme** — codec + port unification + protocol doc
3. **Proto options + plugin Tool** — folder move, generator rename, rebuild plugin
4. **Regenerate HelloWorld** protos
5. **TestHelper + Tests** — folder/project/namespace/file renames
6. **Examples + GateWay**
7. **Docs** — architecture, protocol, spec filename sweep
8. **Verification** — `rg -i crpc` zero hits; `dotnet test orient-dotnet.sln` all pass; run `gen-helloworld.bat`

---

## Risks

| Risk | Mitigation |
| --- | --- |
| Large mechanical diff | Single focused PR; rename before new feature work |
| Missed string in error message | CI grep gate |
| ORNT magic breaks external clients | Document in protocol.md; no external consumers yet |
| Generator test fixtures | Update `OrientGenTestFixtures` proto snippets |

---

## Open Items

None — wire magic uses ORNT (breaking), URL uses `orient://`, zero `crpc` in repo.

---

## Approval

- [x] **Rejected** — full zero-CRPC sweep abandoned; user requested return to pre-zero-CRPC scope.

**Active follow-up:** `docs/superpowers/specs/2026-06-28-orient-tests-tool-rename-design.md` (Tests + Tool only; CRpc protocol names unchanged).
