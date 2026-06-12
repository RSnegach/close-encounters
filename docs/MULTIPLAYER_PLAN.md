# Close Encounters — Multiplayer Plan

> Status as of June 2026. This is a scoping + migration plan, not yet implemented.
> Branch: `multiplayer-foundation`. `main` remains the playable single-player build.

## TL;DR

- **Multiplayer does not work today.** The menus (Host / Join / server browser / Connect-by-IP)
  are a UI shell with `// TODO` stubs. No networking library, no Steam integration, no state
  replication. All three modes (solo/host/join) play identically as single-player-with-bots.
- **You do NOT need to pay for a dedicated server.** The recommended model is a peer-to-peer
  "listen server" (one player hosts) over **Steam Datagram Relay**, which is free for Steam titles.
- **Only mandatory cost:** the one-time **$100 Steam Direct** publishing fee (recoupable). The
  Steamworks SDK and relay are free.
- **Effort:** ~3–6 weeks for a polished networked build. A ~1-week Phase 0 spike validates the
  stack before committing to the heavy phases.

## The key de-risker: vehicles are a clean serializable spec

The scariest part of networking this game looked like the procedurally-built vehicles. It isn't,
because a vehicle is fully described by a small recipe:

```
VehicleData (VehicleSerializer.cs) =
    List<PartEntry> { string id, int[] gridPosition, int[] armorFace?, int rotationSteps }
    + string name, string domain, float forwardAngle
```

`ArenaManager.SpawnVehicle()` **deterministically rebuilds** a vehicle from that data, and
`VehicleSerializer` already turns it into bytes. So we never replicate meshes/joints — we send the
**recipe** (tiny) and every client rebuilds the identical vehicle locally. This collapses the hardest
problem into "send a struct."

## Recommended stack

- **Transport: Unity Netcode for GameObjects (NGO) + the Facepunch.Steamworks transport.**
  Official Unity 6 support; `NetworkObject` / `NetworkVariable` / `ServerRpc` / `ClientRpc` /
  `NetworkTransform`; integrates with the existing scene flow via `NetworkManager` scene management.
  Facepunch routes over Steam P2P + Datagram Relay (free; hides IPs; NAT traversal).
- **Alternative:** Mirror + FizzySteamworks (very mature, also free over Steam relay).
- **Avoid:** Photon (paid CCU caps; bypasses the free Steam relay).

### Architecture

- **Peer-to-peer listen server** — one player hosts, others join by Steam lobby / friend invite.
  No dedicated machine → no recurring cost.
- **Guardrails (standard for casual FFA):** lobby-gated (no mid-match join); **no host migration**
  (host quits → match ends); ~8 players.
- **Authority (pragmatic hybrid):**
  - Your own vehicle: **client-authoritative** movement via `NetworkTransform` (best feel, simplest).
  - Firing & damage: **server-authoritative** — client requests via `ServerRpc`, host validates and
    applies; HP can't be trivially hacked.
  - AI: runs **only on the host**; AI vehicles replicate as transforms; clients are viewers.
  - Win condition: host decides, broadcasts to all.

## Phased plan

### Phase 0 — Spike & go/no-go (~1 week)
Install NGO + Facepunch transport + Steamworks; `steam_appid.txt` (Valve test app **480 / Spacewar**
for dev); a `SteamManager`; ParrelSync for two editor instances. **Goal:** two instances join one
Steam lobby and see a synced object move. Validates the whole stack cheaply before real work.

### Phase 1 — Session & lobby
Wire the existing menu (already built for this): Host → create Steam lobby; Join/browser → lobby list
+ friend invites; networked lobby player list feeding `LobbyUI`; host "Start" drives an NGO scene load
through Builder → Combat for everyone.

### Phase 2 — Per-player loadout + spawn
Each client builds locally, then sends its `VehicleData` recipe to the host (already serializable).
Host spawns one owned `NetworkObject` vehicle per client + N host-owned AI. **Big `ArenaManager`
rewrite:** replace the hardcoded `1 + aiCount` local spawn with "spawn per connected player + AI,"
index by `PlayerId`, attach input/camera only on the owning client (`IsOwner`), `AIController` only on
the host. Each client rebuilds every vehicle's visuals from the replicated recipe.

### Phase 3 — Replicate gameplay
`NetworkTransform` on vehicles (owner-auth players, server-auth AI). Firing → `ServerRpc` → host spawns
networked projectiles / authoritative hits. **Make `DamageSystem` server-authoritative** (HP &
part-destruction as net state; `ClientRpc` for explosions/VFX). The Session-1 screenshake/hitmarker fire
on the owning client off the networked damage event.

### Phase 4 — Lifecycle & polish
Server-authoritative win check + results broadcast; spectator cycles among surviving networked players;
disconnect handling; HUD reads the local owned vehicle.

## What changes in the current code

| Area | Today | Change | Size |
|---|---|---|---|
| `ArenaManager` spawn | 1 player + N AI, local, PlayerId 0 | per-client spawn + AI; ownership; host-only | **Large** |
| `DamageSystem` | static, mutates local HP | server-authoritative + replicated HP/part state | **Large** |
| `PlayerVehicleController` / `PlayerCombatInput` | always drive the one vehicle | gate to `IsOwner`; fire via `ServerRpc` | Medium |
| Vehicle root + `PartNode` | plain GameObject, local HP | add `NetworkObject`/`NetworkTransform`; HP as net state | Medium |
| `GameManager.MatchSettings.playerVehicle` | single loadout | per-player loadout keyed by `PlayerId` | Small |
| Scene transitions | `SceneManager.LoadScene` | NGO scene management (host-driven) | Small |
| `MainMenu` host/join | TODO stubs | Steam lobby create / join / invite | Medium |
| `LobbyUI` player list | local + bots | networked list | Small |
| `WaveManager` | time-based sine, local | sync wave time/seed so buoyancy matches across clients | Small |

Singletons (`VFXManager`, `PartRegistry`) mostly survive — lookup/presentation, triggered via
`ClientRpc` so everyone sees the same explosions.

## Risks

- **Biggest lifts:** the `DamageSystem` authority refactor and the `ArenaManager` multi-spawn.
- **Cheating:** client-authoritative movement is exploitable (speed/teleport). Acceptable for a casual
  Steam brawler — don't over-engineer.
- **Determinism:** sidestepped for movement (clients own their transform). `WaveManager` is the one
  shared sim needing a synced clock.

## Costs

- **$100 one-time Steam Direct** fee to publish (recoupable at ~$1,000 revenue).
- Steamworks SDK + Datagram Relay: **free**.
- **No recurring server cost** with the listen-server model.

---

## Phase 0 — turnkey setup checklist (do these in Unity)

These steps need the Unity Editor + a running Steam client, so they can't be scripted from outside.

1. **Steamworks partner setup** (later, for your real App ID). For development, use **App ID 480**
   (Spacewar) — `steam_appid.txt` at the project root is already set to `480` on this branch.
2. **Install Netcode for GameObjects:** Package Manager → *Add package by name* →
   `com.unity.netcode.gameobjects` (latest 2.x for Unity 6). Let Package Manager pick the version so
   resolution is clean.
3. **Install the Steam transport (Facepunch):** Package Manager → *Add package from git URL* →
   `https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.facepunch`
   (this bundles Facepunch.Steamworks).
   - Mirror alternative: install Mirror from the Asset Store, then FizzySteamworks.
4. **Install ParrelSync (multi-editor testing):** Package Manager → *Add package from git URL* →
   `https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`
5. **Create a `NetworkManager` GameObject** in a bootstrap scene, add the Facepunch transport,
   set the dev App ID to 480.
6. **Spike test:** with Steam running, ParrelSync-clone the project, run two editors, have one create a
   Steam lobby and the other join, and confirm a `NetworkTransform`-driven test object syncs.

Once the spike connects two instances, green-light Phases 1–3 and we implement the netcode against the
now-resolved packages (with the editor open to compile-check).
