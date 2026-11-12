# BeatTogether 1.45.3 crossplay branches

All repositories below use branch `1.45.3`.

| Project | Repository |
| --- | --- |
| BeatTogether.DedicatedServer | https://github.com/FrozenAlex/BeatTogether.DedicatedServer/tree/1.45.3 |
| BeatTogether.UnifiedServer | https://github.com/FrozenAlex/BeatTogether.UnifiedServer/tree/1.45.3 |
| BeatTogether.PC | https://github.com/FrozenAlex/BeatTogether/tree/1.45.3 |
| MultiplayerCore.PC | https://github.com/FrozenAlex/MultiplayerCore/tree/1.45.3 |
| BeatTogether.Quest | https://github.com/FrozenAlex/BeatTogether.Quest/tree/1.45.3 |
| MultiplayerCore.Quest | https://github.com/FrozenAlex/MultiplayerCore.Quest/tree/1.45.3 |
| MultiplayerExtensions.Quest | https://github.com/FrozenAlex/MultiplayerExtensions.Quest/tree/1.45.3 |

## Published branch status — 12 November 2026

This `1.45.3` branch targets Beat Saber **1.45.3_30054**. PC and Quest have
been tested together on the updated LAN UnifiedServer: both join a lobby,
start a Standard custom song, and show the failed screen. The user also tested
Quest MultiplayerExtensions. Full-song completion, late joins, reconnects and
all per-player options are not individually verified.

Runtime fixes include PC menu activation, Quest network configuration setup,
Quest platform identity injection, nonvirtual beatmap-view base calls, dedicated
server state flags, and a results window when all players fail. Validation passed:
19 dedicated-server tests, 10 HTTP tests, 7 PC tests, PC hook/signature audits,
Quest ARM64 builds, QMOD checks, and installed-library hash/load checks.

The PC BSML, SiraUtil and SongCore dependency ports are intentionally **not
published** with these branches. Reproducing the PC test requires compatible
versions of those dependencies. Quest's core pack is likewise separate; use the
versions declared by each QMOD/QPM manifest. Public BeatTogether service and
external credential verification were not validated: the LAN server disabled
authentication for the test. No general production-readiness claim is made.

The notes below preserve the port investigation/build history. Statements there
about pending tests or uncommitted changes describe earlier stages; the verified
scope above is current. The combined repository/build guide is available at
https://github.com/FrozenAlex/BeatTogether.UnifiedServer/blob/1.45.3/docs/crossplay-1.45.3.md.

---


## Build order

1. Clone UnifiedServer recursively on `1.45.3`; its DedicatedServer submodule is pinned to the matching fork commit. Build/test with .NET 6 or a compatible installed SDK.
2. Build the Quest MultiplayerCore port first with QPM and NDK 29; install its native library and shared headers into the local QPM cache, then build BeatTogether.Quest and MultiplayerExtensions.Quest. See their port notes.
3. For PC, supply a local Beat Saber 1.45.3 installation and compatible BSML/SiraUtil/SongCore DLLs. Run MultiplayerCore `build-port.ps1` with `-BeatSaberDir`, `-ModRefsDir` and `-BeatTogetherRepo`; copy-to-game is disabled during the build.

## Re-run server validation

```powershell
dotnet test dependencies/BeatTogether.DedicatedServer/BeatTogether.DedicatedServer.Tests
dotnet test BeatTogether.UnifiedServer.Tests
dotnet publish BeatTogether.UnifiedServer -c Release -o artifacts/server
```

## LAN setup

Set the server advertised host to the PC LAN IP and configure both clients to the same API/status endpoint. Allow the HTTP port and configured UDP slot range only where needed. The tested configuration used four slots and a base UDP port of 31000 (allocated 31001–31004). Do not expose the authentication-disabled test configuration publicly.

The server results timer now applies to Cleared and Failed results; Incomplete-only rounds and a zero timer still return immediately. A two-player lifecycle regression reproduced the missing wait before the fix and passes afterward.

## Scope

Source, tests and port documentation are included. Compiled mod packages, game assemblies, decompiled dumps, local server logs/configurations, and PC dependency port source are not part of these branch pushes.
