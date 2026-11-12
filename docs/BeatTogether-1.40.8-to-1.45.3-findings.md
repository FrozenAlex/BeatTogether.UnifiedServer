# Beat Saber multiplayer and BeatTogether: 1.40.8 to 1.45.3 findings

Prepared 12 November 2026. Standalone sharing copy of the source and binary investigation.

**Implementation follow-up:** the dedicated server has now been updated from
`dev-1.42.0`, and all three Quest mods have local 1.45.3 test QMODs. See the
[implementation and handoff report](multiplayer-1.45.3-implementation-progress.md)
for changes, artifacts, passed host checks and outstanding runtime validation.
The analysis below retains its original investigation baselines.

The port needs coordinated work in MultiplayerCore and the BeatTogether servers. The BeatTogether Quest plugin is mostly server selection and UI; MultiplayerCore owns the network redirection, authentication hooks, packet registration, custom-song loading, and player synchronization that changed substantially. A successful client build alone will not establish compatibility.

This analysis compares Quest **1.40.8_7379** and **1.45.3_30054**. It does not change, build, or install any mod. The first working target should be two 1.45.3 clients and a server implementing the new protocol. Supporting older PC or Quest clients requires explicit translation or separate compatibility groups.

**Repository branch follow-up (12 November 2026).** The initial dedicated-server comparison below used `master`. A subsequent review found partial newer-protocol work in `dev-1.42.0` and an unfinished mixed-version implementation in `dev_1.37.0-1.42.0`. Reuse that work when planning the port. The branch-specific findings and remaining defects are documented at the end; descriptions of the old public server layout below refer to the inspected `master` revision.

**Evidence and scope.** The primary evidence is the two original APKs and their ARM64 `libil2cpp.so` implementations, with matching generated headers from bs-cordl commits `53bed7be6d` and `48c3305e55`. The local PC installation identifies itself as **1.45.2_28848**, so its decompiled assemblies were used as supporting explanations, not as a substitute for the 1.45.3 Quest binaries. In particular, the new generic identity serializer was located and checked directly in the Quest binary. The initial public server comparison used DedicatedServer master at `0693ad056ac52aa334b4c893cee0660257d4b1a9` and MasterServer `c2ca5a8906d3d45928d69a5145544f190308ab03`; deployed production servers were not inspected. No headset or client/server interoperability test was performed.

The local mod baselines are BeatTogether.Quest `7d7a6c5` (2.2.5), MultiplayerCore.Quest `135d2f8` (1.6.7), and MultiplayerExtensions.Quest `b0269cb` (1.1.1). All three working trees were clean before and after the investigation.

**Confirmed packet ID changes.** These are the outer connected-player message types, not the inner MultiplayerSession message types.

| Outer ID | 1.40.8 | 1.45.3 | Required server handling |
| --- | --- | --- | --- |
| 0–9 | SyncTime through PlayerStateUpdate | Same named IDs | Preserve the existing mapping, but update affected payloads below. |
| 10 | PlayerAvatarUpdate | Ping | An old server interprets the new ping as avatar data. |
| 11 | Ping | Pong | Update both parsing and emission. |
| 12 | Pong | GameSpecificMessageType | Dispatch a nested Beat Saber message type. |
| 12 → 0 | Not present | PlayerAvatarUpdate | Avatar updates move from outer ID 10 into this nested category. |
| 12 → 1 | Not present | PlayerControllerDataUpdate | New payload: one `uint8 activeHand`, with None=0, Left=1, Right=2. |

`NetworkConstants.kProtocolVersion` is **9 in both versions**. It cannot distinguish these incompatible layouts. The new `CompatibilityVersion.Get()` returns **`A31AF687`** in the 1.45.3 Quest binary; `Length()` returns 8. This value was resolved from the function's metadata string reference, rather than inferred from the application version. Prefer the game's compatibility API over a hardcoded value in the client.

Evidence: `headers/{old,new}/GlobalNamespace/zzzz__ConnectedPlayerManager_def.hpp`, the new `BeatSaberConnectedPlayerManager` and `MultiplayerActiveHand` headers, and `native/new/GlobalNamespace__PlayerControllerDataPacket.Serialize.0x354ff68.txt` in the original investigation archive described at the end. The inspected master revision still has the old outer enum: [PacketType.cs](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/0693ad056ac52aa334b4c893cee0660257d4b1a9/BeatTogether.DedicatedServer.Messaging/Enums/PacketType.cs).

**Confirmed payload changes.** Sequences below describe the packet body, excluding the surrounding packet framing. `string` means the existing LiteNetLib string encoding; byte arrays retain their variable-length size prefix.

| Packet | 1.40.8 field order | 1.45.3 field order |
| --- | --- | --- |
| GameLift connection request | `string userId`, `string userName`, `bool isConnectionOwner`, `string playerSessionId` | Same four fields, then **`string compatibilityVersion`**. |
| PlayerConnected, outer ID 1 | `uint8 remoteConnectionId`, `string userId`, `string userName`, `bool isConnectionOwner` | Same fields, then **`string compatibilityVersion`**. |
| PlayerIdentity, outer ID 2 | `PlayerStateHash`, `MultiplayerAvatarsData`, `byte[] random`, `byte[] publicEncryptionKey` | **`PlayerStateHash`, `byte[] random`, `byte[] publicEncryptionKey`, `MultiplayerAvatarsData`, `uint8 activeHand`**. |
| PlayerAvatarUpdate | Outer ID 10, avatar payload | Outer ID 12, sub-ID 0, avatar payload. |
| PlayerControllerDataUpdate | Absent | Outer ID 12, sub-ID 1, `uint8 activeHand`. |

PlayerIdentity is a reordering, not merely an appended optional field. The old parser expects avatar data where the new client writes an encryption byte-array length. Likewise, the new client expects compatibility information in server-generated PlayerConnected messages. Updating only the version allowlist would leave these mismatches unresolved.

The new identity envelope is `PlayerIdentityPacket<BeatSaberPlayerIdentityPacketData>`. Its Quest serializer at `0x4c8c884` writes the state hash, random array and public-key array, then calls `BeatSaberPlayerIdentityPacketData.Serialize` at `0x354fed8`, which writes avatars and activeHand. Its reader at `0x4c8c8f0` follows the same order. See `identity-callsites.txt`. The old identity serializer is `0x22c1648`. Connection request serializers are `0x22c6758` and `0x3551a34`; PlayerConnected serializers are `0x22c1578` and `0x35d97b8`.

The inspected master revision's [ConnectionRequestData](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/0693ad056ac52aa334b4c893cee0660257d4b1a9/BeatTogether.DedicatedServer.Messaging/Models/ConnectionRequestData.cs) and [PlayerIdentityPacket](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/0693ad056ac52aa334b4c893cee0660257d4b1a9/BeatTogether.DedicatedServer.Messaging/Packets/PlayerIdentityPacket.cs) still implement the old layouts. They need coordinated read/write changes, alongside the registries and avatar/controller handlers. Preserve each peer's compatibility value for routing and server-generated player announcements.

**Gameplay schemas that appear reusable.** All 40 MenuRpc IDs, all 15 GameplayRpc IDs, and the seven stock MultiplayerSession category IDs retain their names and values. The category mapping remains MenuRpc=0, GameplayRpc=1, NodePoseSyncState=2, ScoreSyncState=3, NodePoseSyncStateDelta=4, ScoreSyncStateDelta=5, OptionalAvatarData=6. MultiplayerCore's custom category remains the mod-defined ID **100**.

The inspected native serializers preserve these field sequences and encodings:

| Payload family | Finding |
| --- | --- |
| BeatmapKey | `string levelID`, `string characteristic`, unsigned varint difficulty. The serializer changes from an explicit interface method to a public method, but keeps its wire encoding. |
| NoteCut, NoteMiss, NoteSpawn, ObstacleSpawn, SliderSpawn | The inspected field order and signed-varint/float/vector encodings remain. Many new byte-writer calls are inlined varint loops, not conversions from varints to fixed bytes. |
| Node pose full and delta | Same three-pose state; the delta packet retains its state/empty flag, signed-varint time offset and conditional state body. |
| Standard score full and delta | Same five signed-varint score components and corresponding delta representation. |
| Player-specific settings and colors | No field-order change found in the inspected serializers. |
| MultiplayerLevelCompletionResults | Same end-state/end-reason wrapper and conditional result body. |
| LevelCompletionResults | The local object changes to a `LevelCompletionResultsBase` and `LevelScore` structure, but the inspected wire serializer retains the existing results sequence ending with `endSongTime`, `invalidated`. New local `startSongTime`, score maxima and obstacle-hit fields are not added to that wire sequence. |

These are static serializer findings, not captured-packet or round-trip compatibility tests. Keep them as the baseline for byte fixtures instead of rewriting every gameplay codec because the generated class layouts changed.

**MultiplayerCore custom messages.** Preserve category 100 and the string packet-name dispatch contract during the port:

- `MpPlayerData`: platform ID string, platform int32, game-version string.
- `MpPerPlayerPacket`: PPDEnabled bool, PPMEnabled bool; `GetMpPerPlayerPacket` has no body.
- `MpNodePoseSyncStatePacket` and `MpScoreSyncStatePacket`: signed-varlong delta-update interval, then full-state interval.
- `MpBeatmapPacket`: hash and song metadata strings, BPM/duration floats, characteristic string, **uint32 difficulty**, followed by the existing requirements, contributors and map-color collections. This difficulty encoding differs from stock BeatmapKey's varint and must not be changed accidentally.

The game's update does not itself require a new schema for those mod-owned messages. The registration and managed interfaces do require porting.

**Matchmaking and authentication.** The 1.45.3 `GetMultiplayerInstanceRequest` removes `singleUseAuthToken` and `authUserId`, and adds `customLocation`. The corresponding JSON names in the newer client model are `single_use_auth_token`, `auth_user_id`, and `custom_location`. Other request fields remain: version, service environment, selection mask, server configuration, user ID, private game secret/code, platform, region latencies, ticket ID and placement ID. `PlayerSessionInfo` additionally gains `ip_address` alongside `dns_name`.

The public [GetMultiplayerInstanceController](https://github.com/BeatTogether/BeatTogether.MasterServer/blob/c2ca5a8906d3d45928d69a5145544f190308ab03/BeatTogether.MasterServer.Api/HttpControllers/GetMultiplayerInstanceController.cs) explicitly requires nonempty SingleUseAuthToken, UserId and AuthUserId when creating a session, and uses AuthUserId to build the platform identity. An unadapted new client request fails that contract. Adding 1.45.3 to its configured version range does not fix authentication.

On the client, `PlatformAuthenticationTokenProvider.GetAuthenticationToken()` is gone. The provider now uses `OculusStudios.Platform.Core.IPlatform` and exposes `GetXPlatformAccessToken`; `_platform` is now an object, while the enum is `_platformType`. The older `IPlatformUserModel` and `OculusPlatformUserModel` types are absent. The old authentication/Pico hook cannot be carried over by just changing an include. Agree on a supported authenticated client/server contract and adapt the provider or matchmaking request path accordingly; do not treat removing the server's checks as the port.

The supporting PC 1.45.2 managed provider implementation uses the cross-platform token as the Graph API request credential. Its exact HTTP transport behavior should be covered in the integration capture, particularly on a re-signed Quest APK. Native request layouts and the removed hook targets are confirmed for 1.45.3; the readable provider flow is also preserved from PC 1.45.2 as supporting evidence.

**Client changes by responsibility.**

| Area | Existing dependency | Port work |
| --- | --- | --- |
| Session and players | `MultiplayerSessionManager`, `IMultiplayerSessionManager`, concrete old `ConnectedPlayerManager` | Use `BeatSaberMultiplayerSessionManager` / `IBeatSaberMultiplayerSessionManager` and the new generic player-manager layer; use `IBeatSaberConnectedPlayer` where avatars/controller data are needed. Update packet registration and delegate types consistently. |
| Custom subserializer | `INetworkPacketSubSerializer<IConnectedPlayer>` | Implement the new `INetworkPacketProcessor<T>` contract, including `PacketProcessedHandler` get/set, with the appropriate connected-player type. Register custom ID 100 as the new `NetworkMessageType`. |
| RPC plumbing | `RpcHandler<T>` | The game now uses `RpcHandler<NetworkMessageType, RpcType, IBeatSaberConnectedPlayer>`. Preserve RPC IDs while adapting the type relationships. |
| Network configuration | `MainSystemInit._networkConfig`, `_multiplayerStatusUrl`, `_forceGameLift` | These old fields are gone. Find/inject the actual `INetworkConfig` during startup, restore server switching, and ensure consumers that construct HTTP clients see the selected server. Do not simply rename `_forceGameLift` to `_useLocalServer`; their meanings differ. |
| Status request | Standalone configurable status URL | The newer path derives `beat_saber_multiplayer_status` from `graphApiBaseUrl` and includes access-token/service-environment query parameters. Preserve custom status endpoints explicitly. New status fields include `use_local_server`, `local_server_port`, `use_gamelift_udp_ping_beacons`; `use_xplatform_auth` is removed. |
| Backend selection and TLS | Platform/GameLift and `ClientCertificateValidator` hook | The alternative enum is now LocalServer; the old certificate-validator type is absent. Reassess the active ENet/Ignorance path rather than restoring a dead hook. |
| DI setup | `MainSystemInit.InstallBindings(container, bool)` | Signature is now `(container, Destination*)`. Rebind entitlement and pose/score replacements at the correct lifetime. |
| Song selection and loading | Custom levels, download/entitlement hooks, old BeatmapKey references | Update SongCore/BeatmapLevel APIs and read-only BeatmapKey references. Exercise missing songs, failed downloads and per-player options. |
| Scene transitions | `MultiplayerLevelScenesTransitionSetupDataSO`, `SceneInfo`, `ScenesTransitionSetupDataSO` | Move to the new transition/destination types. Fix callbacks, no-map spectator startup and MultiplayerExtensions environment selection. |
| Runtime/toolchain | hook 6, 4008 headers, old custom-types/Lapiz/BSML | Rebuild against the workspace's Unity 6 / 4503 headers and compatible hook 9-era dependencies. Verify resolved QPM versions; some local package manifests do not reflect the published version labels. Use the project's NDK/C++ setup and GameActivity launch name. |

The automatic include check found six missing game headers used by MultiplayerCore and four used by MultiplayerExtensions. BeatTogether itself had no missing game include paths, but still needs hook-signature, UI and dependency checks. `missing-includes.json` records the exact list.

Relevant mod entry points: `MultiplayerCore.Quest/src/Hooks/NetworkConfigHooks.cpp`, `PlatformAuthenticationTokenProviderHooks.cpp`, `MainSystemBinderHooks.cpp`, `MethodOverrideHooks/PlayersDataModel.cpp`, `NoLevelSpectatorHook.cpp`, `src/Networking/MpPacketSerializer.cpp`, the custom pose/score managers, and `BeatTogether.Quest/src/UI/ServerSelectionController.cpp`. Audit retained managed references in async download/authentication callbacks while changing those lifetimes.

**Suggested port order and acceptance checks.**

1. Decide whether the initial release isolates new clients or translates old/new protocols. Keep those compatibility groups separate until translations pass byte fixtures.
2. Update the dedicated-server packet registries, connection/player announcements, identity ordering, avatar routing and active-hand support. Add exact fixtures for both versions and ensure each reader consumes the entire intended body.
3. Adapt the authenticated matchmaking contract and status/config routing. Register the new supported game-version range only after that contract works.
4. Port MultiplayerCore's session types, serializer interface, DI hooks, scene transitions and custom-song integrations. Then update BeatTogether's selector/UI and dependencies.
5. Validate two new clients: create/join, all player announcements, avatar changes, controller-hand changes, ping/pong, song recommendation, custom-song download, entitlements, PPD/PPM, countdown, full and delta pose/score updates, finish/results, disconnect/rejoin and spectator paths. Test a failed auth/download attempt as well as success.
6. Port MultiplayerExtensions after the base session/gameplay path works. Then validate the supported PC/Quest combinations; older clients need the translation layer or an intentional compatibility rejection.

The first server fixtures should cover connection requests with and without compatibilityVersion; old/new PlayerConnected; old/new identity order; outer IDs 10/11/12; avatar/controller subtypes; and unchanged representative gameplay payloads. Include negative signed varints, empty delta states, full avatar data, empty and nonempty encryption arrays, and result packets with/without a result body.

**Original investigation archive.** The retained `multiplayer-1.40.8-to-1.45.3-20261112` investigation files contain header snapshots, API diffs, missing includes, symbol indexes, annotated ARM64 disassembly, supporting PC decompilations, public server snapshots and reproducible inspection scripts. `native-provenance.json` records the compared binary hashes: old `edae5059bd5a85ba354eb679e1cdcc3816116d2e2b29b3a16a70c05b56dd07cb`, new `bb560407d856fdd8f00ac3026967b5b10c3af4dbcfa3650ac3fa41d58a3744eb`. This note is a source/binary analysis; it does not establish live-server or headset compatibility.

**Complete outer packet ID mapping.** The unchanged 0–9 range is expanded here so this file can be used without opening the generated headers.

| ID | 1.40.8 | 1.45.3 |
| --- | --- | --- |
| 0 | SyncTime | SyncTime |
| 1 | PlayerConnected | PlayerConnected |
| 2 | PlayerIdentity | PlayerIdentity |
| 3 | PlayerLatencyUpdate | PlayerLatencyUpdate |
| 4 | PlayerDisconnected | PlayerDisconnected |
| 5 | PlayerSortOrderUpdate | PlayerSortOrderUpdate |
| 6 | Party | Party |
| 7 | MultiplayerSession | MultiplayerSession |
| 8 | KickPlayer | KickPlayer |
| 9 | PlayerStateUpdate | PlayerStateUpdate |
| 10 | PlayerAvatarUpdate | Ping |
| 11 | Ping | Pong |
| 12 | Pong | GameSpecificMessageType |

The new Beat Saber subtypes within `GameSpecificMessageType` are:

| Subtype | Message | Body |
| --- | --- | --- |
| 0 | PlayerAvatarUpdate | Existing avatar representation. |
| 1 | PlayerControllerDataUpdate | One byte, `activeHand`: 0=None, 1=Left, 2=Right. |

The surrounding serializer retains length-prefixed, type-dispatched messages. The supporting newer PC implementation writes the nested game-specific subtype inside the existing message body without another independent length prefix. Check the exact framing in byte fixtures when implementing the server; do not accidentally introduce a second length envelope.

**Identity layout in sequence form.** This is the packet body, with field grouping made explicit:

```text
1.40.8 PlayerIdentity:
  PlayerStateHash
  MultiplayerAvatarsData
  byte[] random
  byte[] publicEncryptionKey

1.45.3 PlayerIdentity:
  PlayerStateHash
  byte[] random
  byte[] publicEncryptionKey
  BeatSaberPlayerIdentityPacketData:
    MultiplayerAvatarsData
    uint8 activeHand
```

`MultiplayerAvatarsData` groups the avatar list and supported-avatar `BitMask128`. The new game-specific identity data writes that group before `activeHand`. Moving the avatar group is essential: leaving it at its previous position corrupts the interpretation of the subsequent fields.

The old and new GameLift connection request bodies are:

```text
1.40.8:
  string userId
  string userName
  bool isConnectionOwner
  string playerSessionId

1.45.3:
  string userId
  string userName
  bool isConnectionOwner
  string playerSessionId
  string compatibilityVersion  // "A31AF687" in the inspected Quest build
```

`BasicConnectionRequestHandler` also adds the compatibility string, but its request has a separate initial secret/key string. It must be treated as its own schema; the GameLift four-field prefix above is not a universal connection-request format.

**Additional API and configuration details.**

- The generated generic types include `MultiplayerSessionManager_5`, `IMultiplayerSessionManager_1`, `IMultiplayerSessionManager_4`, and `ConnectedPlayerManager_3`. Their suffixes describe generic arity in generated C++ names. The Beat Saber specializations include `BeatSaberMultiplayerSessionManager`, `IBeatSaberMultiplayerSessionManager`, and `BeatSaberConnectedPlayerManager`.
- `IConnectedPlayer` still exists, but game-specific avatar/controller access moves through `IBeatSaberConnectedPlayer`. The old `ConnectedPlayerManager` name is no longer the old concrete implementation; its retained definition contains the common message enum/static surface.
- The generated RPC implementation changes from `RpcHandler_1` to `RpcHandler_3`. This is a type/interface change, not evidence of new numeric RPC IDs.
- `NetworkConfigSO` loses the old standalone multiplayer-status URL, force-GameLift flag and master-server hostname fields. The new configuration includes `_useLocalServer`, `_localServerPort`, `_customLocation`, and `_remoteAssetsHost`. `graphAppId` changes from a string to an unsigned 64-bit integer.
- In the backend enum used by `UnifiedNetworkPlayerModel`, `GameLift=0` remains. The old alternative `Platform=1` becomes `LocalServer=1`. Equal enum numbers do not imply the same behavior.
- The new scene-transition class is `MultiplayerLevelScenesTransitionSetupData`, without the old `SO` suffix. The surrounding transition/destination changes still require hook and callback review.
- Several hooks that previously used mutable `ByRef<BeatmapKey>` now need read-only/const-reference signatures. Match the actual target signature when porting; a successful include replacement is insufficient.
- `LevelCompletionResults` constructor and local field access need porting to `LevelScore` and the new base/result structure, including the local `startSongTime` input and moved enums/fields. These object changes do not imply corresponding additions to serialized results.
- The existing `IgnoranceClient` hook must be reviewed separately from the removed `ClientCertificateValidator` hook. Removal of the latter does not establish that all transport/TLS interception moved to one replacement method.
- For Android launch/restart scripts, the 1.45 activity is `com.beatgames.beatsaber/com.unity3d.player.UnityPlayerGameActivity`. The old `UnityPlayerActivity` name is no longer valid.
- The MultiplayerCore classes named `MpNodePoseSyncStatePacket` and `MpScoreSyncStatePacket` exchange synchronization update intervals. They are not the stock per-frame pose/score payloads, despite their names.

**Exact missing game-header list.** This was an include-path scan against the compared generated headers, not a compile test. It does not capture changed methods, fields, generic parameters or transitive dependencies.

MultiplayerCore:

```text
GlobalNamespace/ClientCertificateValidator.hpp
GlobalNamespace/IMultiplayerSessionManager.hpp
GlobalNamespace/IPlatformUserModel.hpp
GlobalNamespace/MultiplayerLevelScenesTransitionSetupDataSO.hpp
GlobalNamespace/MultiplayerSessionManager.hpp
GlobalNamespace/OculusPlatformUserModel.hpp
```

MultiplayerExtensions:

```text
GlobalNamespace/IMultiplayerSessionManager.hpp
GlobalNamespace/MultiplayerLevelScenesTransitionSetupDataSO.hpp
GlobalNamespace/SceneInfo.hpp
GlobalNamespace/ScenesTransitionSetupDataSO.hpp
```

No missing game-header paths were found for BeatTogether itself. This does not establish build compatibility.

**Evidence provenance and reproducibility.**

| Evidence | Baseline |
| --- | --- |
| Old Quest build | `1.40.8_7379`, original APK |
| New Quest build | `1.45.3_30054`, original APK |
| Old generated headers | bs-cordl commit `53bed7be6d` |
| New generated headers | bs-cordl commit `48c3305e55` |
| Supporting PC installation | `1.45.2_28848`; not a 1.45.3 PC comparison |
| BeatTogether Quest source | `7d7a6c5`, package 2.2.5 |
| MultiplayerCore Quest source | `135d2f8`, package 1.6.7 |
| MultiplayerExtensions Quest source | `b0269cb`, package 1.1.1 |
| Dedicated server source | `0693ad056ac52aa334b4c893cee0660257d4b1a9` |
| Master server source | `c2ca5a8906d3d45928d69a5145544f190308ab03` |

The native comparison reads `lib/arm64-v8a/libil2cpp.so` and `assets/bin/Data/Managed/Metadata/global-metadata.dat` from each original APK. Addresses below are virtual addresses in the matching ARM64 library, not addresses to reuse across other builds.

| Native evidence | 1.40.8 | 1.45.3 |
| --- | --- | --- |
| GameLift connection request Serialize | `0x22c6758` | `0x3551a34` |
| PlayerConnected Serialize | `0x22c1578` | `0x35d97b8` |
| PlayerIdentity Serialize | `0x22c1648` | `0x4c8c884` |
| New PlayerIdentity Deserialize | — | `0x4c8c8f0` |
| BeatSaberPlayerIdentityPacketData Serialize | — | `0x354fed8` |
| BeatSaberPlayerIdentityPacketData Deserialize | — | `0x354ff18` |
| PlayerControllerData Serialize | — | `0x354ff68` |
| LevelCompletionResults Serialize | `0x2720dc8` | `0x3a31e94` |
| CompatibilityVersion.Get | — | `0x3550f4c` |
| CompatibilityVersion.Length | — | `0x3550f90` |

The generic identity serializer was resolved through native calls to the concrete identity-data serializer; it was not assumed from generated headers that omit generic method addresses. `CompatibilityVersion.Get()` was resolved through its IL2CPP metadata string reference and gives the exact eight-character value `A31AF687`.

The original investigation archive retains:

- `compare_headers.py`, `api-diff.txt`, `api-changes.json`, and `missing-includes.json` for the API comparison.
- `headers/old` and `headers/new` for the matching generated declarations.
- `inspect_native.py`, native method/symbol indexes, `native-provenance.json`, and annotated disassembly under `native/old` and `native/new`.
- `probe_identity.py` and `identity-callsites.txt` for the generic identity serializer and compatibility literal.
- Supporting PC decompilations and fixed-revision public server source snapshots.

The API diff includes address/offset and generated-format changes. Its raw change count must not be presented as a count of substantive protocol changes. Similarly, the disassembly inventory is not a claim that every extracted method was manually audited. The seven stock session IDs, 40 menu RPC IDs, 15 gameplay RPC IDs, protocol value 9 in both versions, and new compatibility literal were checked explicitly.

This Markdown file contains the findings and source references; the local binary/header/disassembly archive is separate and is not embedded in it.

**Remaining questions and validation limits.**

1. The authentication bridge for the newer provider and BeatTogether's master API still needs implementation and an actual request/response capture, including a re-signed Quest APK.
2. No golden-byte encode/decode fixtures or live network captures were produced in this investigation. The unchanged gameplay formats are based on the inspected serializers, not a guarantee covering every multiplayer message.
3. Two-client lobby, gameplay, spectator, reconnect and results behavior has not been tested. No mod was ported, built, installed or committed as part of this analysis.
4. Public source was inspected at the revisions listed above. Deployed BeatTogether production behavior, unpublished changes and current hosted configuration were not inspected.
5. PC 1.45.3 behavior and mixed-version crossplay were not independently verified. The PC 1.45.2 material only supports explanations of the newer code structure.
6. A newer-only server path is the smaller initial target. Supporting old and new clients in one lobby additionally requires per-peer compatibility decisions and translation of the incompatible connection, identity, announcement and game-specific message layouts.

**DedicatedServer development-branch review — 12 November 2026.**

The linked repository's default branch is still `master` at `0693ad056ac52aa334b4c893cee0660257d4b1a9`, matching the original report. Reviewing its other branches changes the estimate of work already available: the newer packet layout has been partially implemented outside master. None of the inspected development snapshots establishes complete 1.45.3 support.

| Branch | Inspected commit | Relevant source state |
| --- | --- | --- |
| `master` | `0693ad056ac52aa334b4c893cee0660257d4b1a9` | Original report baseline: old outer IDs and identity format. |
| `dev` | `1b29af6d075446bdab217ab3939b954eb53d80bd` | Its inspected PacketType enum still has the old outer IDs. No full audit of this branch was performed. |
| `dev-1.42.0` | `754a67c86debb617748a3a228552092742e319eb` | New outer IDs, reordered identity envelope, and a serialized compatibility string in PlayerConnected. Several 1.45.3 requirements remain missing. |
| `dev_1.37.0-1.42.0` | `e79819ac2253d4dd571d0ffb065e5ed5d4e58d49` | Introduces versioned registries, separate old/new identity and ping/pong classes, and per-version recipient groups. The implementation has source-level gaps and defects. |

**What the 1.42 branches already cover.**

Both newer branches map Ping=10, Pong=11 and GameSpecificMessageType=12. Both move the identity random/key arrays ahead of the game-specific avatar data. The single-version `dev-1.42.0` branch also reads/writes the appended PlayerConnected string, named `CompatabilityVersion` in its source. These changes can serve as a starting point for the 1.45.3 port. Sources: [packet IDs](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/754a67c86debb617748a3a228552092742e319eb/BeatTogether.DedicatedServer.Messaging/Enums/PacketType.cs), [identity envelope](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/754a67c86debb617748a3a228552092742e319eb/BeatTogether.DedicatedServer.Messaging/Packets/PlayerIdentityPacket.cs), [PlayerConnected](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/754a67c86debb617748a3a228552092742e319eb/BeatTogether.DedicatedServer.Messaging/Packets/PlayerConnectedPacket.cs).

**Remaining 1.45.3 work, mapped to repository files.**

| Files / area | Observed state | Required work |
| --- | --- | --- |
| `Messaging/Models/ConnectionRequestData.cs` | Both branches still read/write only the four old GameLift fields. | Read and retain the appended compatibility string, with an explicit policy for older clients. |
| `Messaging/Packets/PlayerConnectedPacket.cs` | `dev-1.42.0` serializes the string but defaults it to empty. The mixed branch comments out both its read and write. | Select the correct format for each recipient and populate compatibility information. |
| `Kernel/DedicatedInstance.cs` | The inspected join/announcement builders do not set the compatibility property. | Update new-player announcements, server-owner announcements and existing-player replay to the joining client. |
| `Messaging/Models/BeatSaberPlayerIdentityPacketData.cs` | Both versions serialize avatars only. | Append/read `uint8 activeHand` for the confirmed 1.45.3 schema; preserve the value in player state and reconstructed identities. |
| `Messaging/Registries/PacketRegistry.cs` | Neither inspected branch registers a game-specific subregistry at outer ID 12. | Register avatar subtype 0 and controller subtype 1, then add the corresponding state handling. |
| `Kernel/PacketHandlers/PlayerIdentityPacketHandler.cs`, `Kernel/Player.cs` | Identity handling stores avatar/state/encryption fields but has no active-hand field. | Preserve controller state for subsequent announcements and late joins. |
| `Kernel/PacketSource.cs`, `Kernel/PacketDispatcher.cs` | Raw routing can forward an incoming body unchanged to other peers. | Keep raw forwarding only where the recipients accept that exact format; translate or separate incompatible peers. |

Paths in this table abbreviate the project prefix: `Messaging/` means `BeatTogether.DedicatedServer.Messaging/`; `Kernel/` means `BeatTogether.DedicatedServer.Kernel/`.

Sources: [connection request](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/754a67c86debb617748a3a228552092742e319eb/BeatTogether.DedicatedServer.Messaging/Models/ConnectionRequestData.cs), [mixed-branch PlayerConnected](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Messaging/Packets/PlayerConnectedPacket.cs), [join announcements](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/754a67c86debb617748a3a228552092742e319eb/BeatTogether.DedicatedServer.Kernel/DedicatedInstance.cs#L366-L483), [identity data](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/754a67c86debb617748a3a228552092742e319eb/BeatTogether.DedicatedServer.Messaging/Models/BeatSaberPlayerIdentityPacketData.cs), [single-version registry](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/754a67c86debb617748a3a228552092742e319eb/BeatTogether.DedicatedServer.Messaging/Registries/PacketRegistry.cs), [mixed-version registry](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Messaging/Registries/PacketRegistry.cs), [packet parsing and raw routing](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Kernel/PacketSource.cs).

Missing parser registration does not mean every new avatar/controller update is necessarily dropped: `PacketSource` can skip an unknown message while forwarding its original bytes when the routing header requests it. That can help peers sharing a format, but it does not update the server's cached state or translate the message for older recipients. This distinction matters when testing avatar changes, late joins and mixed lobbies.

**Specific defects in the mixed-version branch.**

These findings are from source inspection at `e79819ac2253d4dd571d0ffb065e5ed5d4e58d49`; they were not validated by building or running that branch.

1. **The version-range lookup table is never populated.** `SetupVersioning` collects ranges in the local `versionRangesConstructor` dictionary but does not publish them to the `versionRanges` field read by `GetVersionIndex`. Consequently that lookup falls back to bucket 0, including for IDs whose newer factories were registered under a later bucket. Merely declaring old/new ranges in PacketRegistry does not make receive-side selection work. [version lookup and setup](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Messaging/Abstractions/BaseVersionedPacketRegistry.cs#L13-L119)

2. **Recipient version does not select a different serialized body.** `TryGetPacketIds` immediately returns the IDs registered for the concrete packet type without consulting the requested version. `PacketDispatcher.WriteOne` then invokes that same object's `WriteTo`. Identity handlers pass the original packet object onward. Grouping recipients by version therefore does not, by itself, convert an old identity into a new one or vice versa. [packet ID lookup](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Messaging/Abstractions/BaseVersionedPacketRegistry.cs#L210-L238), [packet writer](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Kernel/PacketDispatcher.cs#L283-L306)

3. **Late-join identity replay constructs the old class unconditionally.** `DedicatedInstance` builds `PlayerIdentityPacket_1_40_8` for existing players and sends it to the newly joined player. A new recipient requires the new body and, for 1.45.3, the trailing active-hand field. [late-join replay](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Kernel/DedicatedInstance.cs#L462-L483)

4. **Removing a player adds it to the version group.** `PlayerRegistry.RemovePlayer` calls `players.Add(player)` in the version-group branch instead of removing the departing player; its alternate branch also adds a new group containing that player. This can retain disconnected recipients and create duplicate group entries. Correct group cleanup before relying on version-group broadcasting. [version-group removal](https://github.com/BeatTogether/BeatTogether.DedicatedServer/blob/e79819ac2253d4dd571d0ffb065e5ed5d4e58d49/BeatTogether.DedicatedServer.Kernel/PlayerRegistry.cs#L97-L117)

These defects make the mixed-version branch a reference for ongoing work, not evidence that cross-version translation is complete. The single-version 1.42 branch provides a simpler reference for the newer outer IDs and identity envelope. An initial 1.45.3-only implementation still needs the compatibility, active-hand, nested-message and matchmaking changes listed above, followed by the fixture and two-client checks in this report.

The authenticated HTTP matchmaking issue remains separate: the previously inspected request validation lives in `BeatTogether.MasterServer`, so updating DedicatedServer alone does not resolve that client/master contract.

The 28 fetched source snapshots and branch inventory for this follow-up are retained separately in `upstream/development-branch-review.json` within the investigation archive. No remote files, branches, issues or pull requests were changed.
