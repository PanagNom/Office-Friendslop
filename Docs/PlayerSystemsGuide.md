# Office Friendslop: Player Systems Explained

This guide explains, in detail, everything that has been built in the project so far: what each part does, why it exists, and how the parts work together. It assumes no prior knowledge of the code and only a little knowledge of Unity or multiplayer programming. Every concept is explained the first time it shows up.

- **Covers:** the project as of commit `c8d3c1c` ("Nameplate first implementation"), 29 October 2026.
- **Engine:** Unity 6 (6000.6.4f1), Universal Render Pipeline (URP).
- **Networking:** Unity Netcode for GameObjects 2.13 ("Netcode" or "NGO" below), Unity Transport.
- **Testing tool:** Multiplayer Play Mode (MPPM).

If you only want the short version, read [Section 1](#1-the-big-picture) and [Section 6](#6-how-it-all-fits-together-step-by-step-timelines).

---

## Table of contents

1. [The big picture](#1-the-big-picture)
2. [Background concepts you need](#2-background-concepts-you-need)
3. [The test scene: Testing.unity](#3-the-test-scene-testingunity)
4. [The Player prefab](#4-the-player-prefab)
5. [The scripts, one by one](#5-the-scripts-one-by-one)
6. [How it all fits together: step-by-step timelines](#6-how-it-all-fits-together-step-by-step-timelines)
7. [Who runs what: the "copies" table](#7-who-runs-what-the-copies-table)
8. [Problems we hit and what they teach](#8-problems-we-hit-and-what-they-teach)
9. [Current status, known issues and next steps](#9-current-status-known-issues-and-next-steps)
10. [File map](#10-file-map)

---

## 1. The big picture

### What works today

You can open the test scene, type a name, and press **Start Host**. One or more other players (usually extra editor windows from Multiplayer Play Mode) can then type their own names and press **Start Client** to join. Each player:

- appears as a capsule at one of two spawn points,
- sees the world in **first person** through their own camera,
- turns and looks up and down with the **mouse**,
- walks with **W/A/S/D** in the direction they are facing,
- has their **name** sent to everyone else.

A floating **nameplate** above each player's head is half built. The name already reaches the nameplate, but the plate doesn't yet face the camera, fade with distance, or hide itself for its own player (see [Section 9](#9-current-status-known-issues-and-next-steps)).

### The pieces at a glance

```
 ┌──────────────────────────── Testing.unity (the scene) ────────────────────────────┐
 │                                                                                   │
 │  NetworkingDebug ── NetworkDebugUI ──┐  name text field + Start Host/Client        │
 │                                      │                                            │
 │                                      ▼                                            │
 │                              LocalPlayerProfile  (static, saves name in PlayerPrefs)
 │                                                                                   │
 │  NetworkManager ── NetworkManager + UnityTransport   (starts host/client,         │
 │                         │                              spawns a Player per person)│
 │                         ▼                                                         │
 │  GameSystems ── PlayerSpawnManager   (moves each new Player to a spawn point)     │
 │  SpawnPoints ── Spawn_01, Spawn_02                                                │
 │  Camera (menu/overview camera, switched off when your player spawns)             │
 │  Ground, Directional Light                                                        │
 └───────────────────────────────────────────────────────────────────────────────────┘

 ┌──────────────────────────── Player.prefab (one per person) ───────────────────────┐
 │  Player (root)                                                                    │
 │   ├─ NetworkObject            "this object exists on the network"                 │
 │   ├─ NetworkTransform         copies position/rotation from owner to everyone     │
 │   ├─ CharacterController      collision-aware movement                            │
 │   ├─ PlayerInput              reads keyboard/mouse via Player.inputactions        │
 │   ├─ PlayerMovementController WASD movement (owner only)                          │
 │   ├─ PlayerCameraController   own camera + mouse look (owner only)                │
 │   ├─ PlayerInfo               the player's name, synced to everyone               │
 │   ├─ Capsule                  the visible body                                    │
 │   ├─ PlayerCamera             first-person camera (inactive until owner spawns)   │
 │   └─ Nameplate ── PlayerNameplate   shows the name above the head (in progress)   │
 │        └─ Canvas → Image → Text (TMP)                                             │
 └───────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Background concepts you need

This section is long on purpose. Almost every confusing bug in multiplayer comes from misunderstanding one of these ideas.

### 2.1 Unity basics

**GameObject.** Anything that exists in a scene: a light, the ground, a player. On its own a GameObject does nothing; it's a container.

**Component.** A piece of behaviour or data attached to a GameObject. A `Camera` component makes a GameObject render the world, a `MeshRenderer` makes it visible, and a script you write is also a component. Every GameObject has at least a **Transform** component, which holds its position, rotation and scale.

**Hierarchy, parent and child.** GameObjects can be nested. A child moves, rotates and scales with its parent. `localPosition` is a child's position *relative to its parent*. For example, the PlayerCamera's `localPosition` of `(0, 0.7, 0)` means "0.7 m above the centre of the Player".

**MonoBehaviour.** The base class for normal Unity scripts. Unity calls certain methods on it automatically:

| Method | When Unity calls it |
|---|---|
| `Awake()` | Once, when the object is created. |
| `Start()` | Once, just before the object's first `Update`. |
| `Update()` | Every frame. |
| `LateUpdate()` | Every frame, after *all* `Update`s have run. Good for cameras and things that follow other things. |
| `OnGUI()` | Several times per frame, for drawing quick "immediate mode" debug UI. |
| `OnDestroy()` | When the object is destroyed. |

**Serialized fields and `[SerializeField]`.** A field Unity saves with the scene or prefab, and shows in the Inspector so you can set it by dragging things in. `public` fields are serialized automatically. `private` fields are only serialized if `[SerializeField]` is written above them. **`[SerializeField]` applies only to the single field directly below it.**

**Prefab.** A saved, reusable GameObject template (`.prefab` file). The Player is a prefab: Netcode creates a fresh copy ("instance") of it for each person who joins.

**Prefab Mode.** The special editor view you get by double-clicking a prefab. You edit the prefab asset itself there, instead of a copy in a scene.

**Scene.** A level: a saved set of GameObjects (`.unity` file). This project's test scene is `Testing.unity`.

**`Camera.main`.** A shortcut that returns the first *enabled* camera tagged `MainCamera`. If several enabled cameras have that tag, which one you get is unpredictable.

**Meters.** The project uses 1 Unity unit = 1 meter (see `CONVENTIONS.md`).

### 2.2 How this game's multiplayer is shaped

**Client-host topology.** One player's game is both the **server** (the authority that runs the shared game) and a **client** (a normal player). That player is the **host**. Everyone else is a **client** who connects to the host. There is no separate dedicated server.

**The most important idea: everyone has a copy of everyone.** When three people are playing, each of the three computers has **three Player objects**: one for themselves and one for each other person. These copies are separate objects in separate programs. Netcode's job is to keep them in agreement. When you read code, always ask: *"Which copy is running this line, and on whose computer?"*

```
   Host's computer                       Client's computer
 ┌─────────────────────────┐           ┌─────────────────────────┐
 │ Host's Player  (mine)   │◄─────────►│ Host's Player  (remote) │
 │ Client's Player (remote)│◄─────────►│ Client's Player (mine)  │
 └─────────────────────────┘           └─────────────────────────┘
       network keeps each pair of copies in sync
```

**NetworkManager.** The central Netcode component in the scene. It starts the host or client, keeps track of who is connected, and spawns a Player prefab for each person. `NetworkManager.Singleton` is how scripts find it.

**Transport.** The low-level part that actually sends bytes over the network. This project uses **Unity Transport** set to `127.0.0.1:7777`. `127.0.0.1` means "this same computer", which is perfect for Multiplayer Play Mode but means friends on *other* computers can't join yet.

**NetworkObject.** A component that marks a GameObject as "exists on the network". Netcode gives it an ID so every computer knows which copy is which.

**Spawning.** Making a NetworkObject exist on the network. Spawning on the server creates matching copies on all clients. A copy that joins later still receives everything that's already spawned.

**NetworkBehaviour.** Like a MonoBehaviour, but for scripts on a NetworkObject. It adds:

| Member | Meaning |
|---|---|
| `OnNetworkSpawn()` | Called on *every* copy once it's spawned. This is the safe place for network setup. **Before this, `IsOwner` and friends are not reliable and RPCs can't be sent.** |
| `OnNetworkDespawn()` | Called on every copy just before it's removed from the network, for example when its player disconnects or when you stop Play. |
| `IsOwner` | `true` only on the copy that belongs to *this* computer's player. |
| `OwnerClientId` | The ID of the player who owns this object. The host is always client `0`, and clients get `1`, `2`, `3`, … |
| `IsServer` | `true` if this computer is the server. On the host, that's true for *every* copy. |

**Ownership vs. server.** These are two different things, and mixing them up caused one of our bugs ([Section 8.1](#81-the-name-only-worked-for-the-host)). Here's how it works out with a host and one client:

| Copy | Its owner | Is the copy's computer the server? |
|---|---|---|
| Host's player, on the host's computer | host (0) | yes |
| Client's player, on the host's computer | client (1) | yes |
| Client's player, on the client's computer | client (1) | no |
| Host's player, on the client's computer | host (0) | no |

**Authority.** Who is allowed to change something. In this project, **movement is owner-authoritative**: your own computer decides where your player is and tells everyone else. That makes movement feel instant for you. Names, on the other hand, are **server-authoritative**: only the server may set them, so it can check them first.

**NetworkTransform.** A component that copies a Transform's position and rotation from the authority (here, the owner) to every other copy. Remote copies **interpolate**, which means they smoothly blend between received positions so movement doesn't look jumpy.

**RPC (Remote Procedure Call).** A method you call on one computer that actually *runs on another*. In Netcode 2.x you mark it with `[Rpc(SendTo.X)]`, and its name must end in `Rpc`. This project uses two kinds:
- `SendTo.Server`: runs on the server. On the host that's the host's own computer, so the call just runs locally.
- `SendTo.Owner`: runs on the computer that owns the object.

`InvokePermission = RpcInvokePermission.Owner` means "only the owner of this object may call this RPC".

**NetworkVariable.** A variable whose value is automatically copied to every copy of the object, *including players who join later*. It has a **read permission** (who may read it) and a **write permission** (who may change it). `OnValueChanged` is an event that fires on each copy when the value changes there.

**`FixedString64Bytes`.** A NetworkVariable can't hold a normal C# `string`, because a string's size isn't fixed. `FixedString64Bytes` is a fixed-size text type from `Unity.Collections` that holds up to 61 bytes of UTF-8 text. English letters take 1 byte each, Greek letters 2, and some symbols 3. That's why names are limited to 20 characters: 20 × 3 = 60 bytes always fits.

**GlobalObjectIdHash.** A number stored on every network prefab's NetworkObject that identifies *which prefab* it is, the same number on every computer. When the server says "spawn prefab 2488557847", each client looks up the prefab with that number. The correct value for our Player prefab is **2488557847**.

**NetworkConfig check.** When a client connects, it sends a fingerprint of its network settings. That fingerprint covers the tick rate, connection approval, scene management, and the GlobalObjectIdHash of every network prefab. If the host's fingerprint is different, the host logs **"NetworkConfig mismatch"** and disconnects the client. See [Section 8.4](#84-networkconfig-mismatch-clients-rejected-on-join).

**Tick rate.** How many times per second Netcode sends updates. The project uses **30**.

### 2.3 Input System

Unity's (new) **Input System** separates *what the player wants to do* from *which key does it*.

- An **Input Actions asset** (`Player.inputactions`) lists **actions** such as `Move` and `Look`.
- Each action has **bindings**: the physical controls that trigger it. `Move` is bound to W/A/S/D, and `Look` to the mouse's movement (`<Mouse>/delta`).
- A **PlayerInput** component on the Player loads this asset, so scripts can ask it for an action by name and read its value. For example, `actions["Move"].ReadValue<Vector2>()` gives `(x, y)`, where x is left/right and y is forward/back.

### 2.4 PlayerPrefs

Unity's built-in way to save small values on the player's computer so they survive restarts. It works like a dictionary with text keys, and it holds `string`, `int` and `float` values. On Windows it's stored in the registry. It's used here to remember the last name you typed. It is **not** shared with other players, which is why the name also has to be sent over the network. Unity doesn't allow PlayerPrefs calls in field initializers or constructors ([Section 8.2](#82-playerprefs-in-a-field-initializer)).

### 2.5 World-space UI and TextMeshPro

A **Canvas** is the root of a piece of Unity UI. In **World Space** mode it's a flat object in the 3D world, like a sign, instead of being drawn over the screen. In world space, 1 canvas unit is 1 meter, so canvases are usually scaled down to about `0.01`. **TextMeshPro** (`TMP_Text` / `TextMeshProUGUI`) is Unity's high-quality text component. The nameplate is a world-space Canvas with an Image (the box) and a TextMeshPro text (the name).

### 2.6 Multiplayer Play Mode (MPPM)

**Window → Multiplayer → Multiplayer Play Mode** lets you run extra "virtual players" next to the main editor. Each one is a full extra copy of the editor. When you press Play, all of them play together on your one computer.

Two practical consequences:
- Virtual players load **assets and scenes as saved on disk**, so save before pressing Play.
- They run on the same computer and share PlayerPrefs, so each window may show the same saved name. Retype it in each window.

---

## 3. The test scene: Testing.unity

File: `Assets/_Game/Scenes/Test/Testing.unity`

| GameObject | Components | What it's for |
|---|---|---|
| **NetworkManager** | `NetworkManager`, `UnityTransport` | The heart of multiplayer. Settings: Player Prefab = `Player`, Tick Rate = 30, Connection Approval = off, Scene Management = on, Force Same Prefabs = on, Topology = client-server. Transport: address `127.0.0.1`, port `7777`. |
| **NetworkingDebug** | `NetworkDebugUI` | Draws the name field and the Start Host / Start Client buttons. |
| **GameSystems** | `PlayerSpawnManager` | Moves each newly spawned player to a spawn point. Its `spawnPoints` list contains `Spawn_01` and `Spawn_02`. |
| **SpawnPoints** | (just a Transform) | A parent folder for the spawn points. |
| **Spawn_01** | Transform at `(1, 1, 1)` | Spawn position for clients with an even ID: the host (0), client 2, … |
| **Spawn_02** | Transform at `(-0.84, 1, 2)` | Spawn position for clients with an odd ID: client 1, client 3, … |
| **Camera** | `Camera`, `AudioListener`, URP camera data, tagged `MainCamera` | An overview camera used before you join, so the menu has a background. Switched off when your own player spawns. |
| **Ground** | Mesh + `MeshCollider` | The floor. The collider stops the CharacterController from falling through. |
| **Directional Light** | `Light` | Lights the scene. |

The spawn points sit at height `y = 1` because the Player's centre is 1 m above its feet (the capsule is 2 m tall). So a player spawned there stands exactly on the ground at `y = 0`.

---

## 4. The Player prefab

File: `Assets/_Game/Prefabs/Player/Player.prefab`. Netcode creates one copy of this per player, on every computer.

```
Player                         root, local position (0, 1, 0), tag "Player"
├── Capsule                    the visible 2 m tall body (MeshFilter + MeshRenderer)
├── PlayerCamera               INACTIVE in the prefab · local position (0, 0.7, 0) · tag MainCamera
│                              Camera (FOV 70, near clip 0.1) + AudioListener
└── Nameplate                  local position (0, 1.35, 0)  ← PlayerNameplate script
    └── Canvas                 World Space Canvas, scale 0.1
        └── Image              rounded-rectangle box (UISprite, Sliced), scale 0.1, 300 × 80
            └── Text (TMP)     the name, TextMeshPro, font size 24, centred, no wrapping
```

### Components on the Player root

| Component | Plain-language job | Important settings |
|---|---|---|
| **NetworkObject** | Makes the Player a networked object with an ID. | `GlobalObjectIdHash` must be `2488557847` (see [8.4](#84-networkconfig-mismatch-clients-rejected-on-join)). |
| **NetworkTransform** | Sends the owner's position and rotation to everyone else and smooths them on remote copies. | Authority Mode = **Owner**, syncs position and rotation on all axes, Interpolate = on. |
| **CharacterController** | Unity's built-in "walk around without falling through walls" body. You call `Move(...)`, and it slides along whatever it hits. | Height 2, radius 0.5, centre `(0, 0, 0)`. |
| **PlayerInput** | Loads `Player.inputactions`, so scripts can read `Move` and `Look`. | Default action map: `Player`. |
| **PlayerMovementController** | Reads `Move` and walks the player. Only runs on the owner's copy. | Speed = 5 m/s. |
| **PlayerCameraController** | Turns on your own camera, locks the cursor, and handles mouse look. Only active on the owner's copy. | Player Camera = the `PlayerCamera` child, Look Sensitivity = 0.1, Max Pitch = 85°. |
| **PlayerInfo** | Holds the player's name as a NetworkVariable. | none |

### Heights, explained

The root sits at the capsule's centre, which is 1 m above the feet.
- `PlayerCamera` at `+0.7` → eyes at **1.7 m** above the ground.
- `Nameplate` at `+1.35` → **2.35 m**, just above the 2 m tall head.

---

## 5. The scripts, one by one

For each script: **where it lives**, **which copies run it**, **what it does line by line**, and **what it connects to**.

---

### 5.1 `NetworkDebugUI`: the join menu

File: `Assets/_Game/Scripts/Networking/NetworkDebugUI.cs`
Lives on: `NetworkingDebug` in the scene. A normal MonoBehaviour, not networked.

**Purpose:** a minimal menu for testing. It shows a name text field and two buttons, and only until you're connected.

**How it works:**
1. `Start()` copies the saved name into `_nameInput`, so the field shows the name you used last time: `_nameInput = LocalPlayerProfile.PlayerName`. This happens in `Start()` rather than at the field declaration because PlayerPrefs can't be used that early ([8.2](#82-playerprefs-in-a-field-initializer)).
2. `OnGUI()` runs several times per frame:
   - If there's no `NetworkManager`, it does nothing.
   - If this computer is neither a client nor a server (not connected yet), it draws:
     - a **text field**, limited to `LocalPlayerProfile.MaxNameLength` (20) characters;
     - a **Start Host** button, which saves the typed name and calls `NetworkManager.Singleton.StartHost()`;
     - a **Start Client** button, which saves the typed name and calls `NetworkManager.Singleton.StartClient()`.
   - Once connected, it draws nothing, so the menu disappears.

**Connects to:** `LocalPlayerProfile` (reads and saves the name) and `NetworkManager` (starts the session). The name must be saved *before* starting, because `PlayerInfo` reads it the moment your player spawns.

---

### 5.2 `LocalPlayerProfile`: "my settings on this computer"

File: `Assets/_Game/Scripts/Core/LocalPlayerProfile.cs`
A `static class`: there's only ever one, it isn't attached to any GameObject, and any script can use it.

**Purpose:** store and clean up *your* name on *your* computer.

**Members:**
- `MaxNameLength = 20`: the character limit, shared by the text field and the cleaning code.
- `PlayerName`, a property:
  - *get* reads `PlayerPrefs.GetString("PlayerName")`. That returns `""` if nothing is saved.
  - *set* cleans the value with `CleanName`, then saves it with `PlayerPrefs.SetString`.
  - It's a property rather than a stored field, so PlayerPrefs is only touched when you actually read or write it.
- `CleanName(string)`: returns `""` for empty or whitespace-only input. Otherwise it trims spaces at both ends and cuts the name to 20 characters.

**Connects to:**
- `NetworkDebugUI`, which reads and saves the name.
- `PlayerInfo`, which reads `PlayerName` on the owner, and calls `CleanName` on the server to double-check what a client sent.

---

### 5.3 `PlayerSpawnManager`: putting players at spawn points

File: `Assets/_Game/Scripts/Core/PlayerSpawnManager.cs`
Lives on: `GameSystems` in the scene. A normal MonoBehaviour. Only does work on the server.

**Purpose:** when a player joins, move their Player to a spawn point instead of leaving it at the prefab's default position.

**How it works:**
1. `Start()` subscribes to `NetworkManager.Singleton.OnClientConnectedCallback`. This event fires on the server every time a client finishes connecting, **including the host's own local client (ID 0)** right after Start Host. `OnDestroy()` unsubscribes.
2. `OnClientConnected(clientId)`:
   - Returns immediately if this computer isn't the server.
   - Looks up the connected client and its `PlayerObject` (the Player that Netcode just spawned for them).
   - Picks a spawn point with `clientId % spawnPoints.Length`. With 2 spawn points, IDs 0, 2, 4 use `Spawn_01` and IDs 1, 3, 5 use `Spawn_02`.
   - Calls `TeleportToSpawnRpc(position, rotation)` on that player's `PlayerMovementController`.

**Why an RPC to the owner, instead of moving the player directly?** Movement is owner-authoritative ([2.2](#22-how-this-games-multiplayer-is-shaped)). The server isn't allowed to set the client's position, because the client's NetworkTransform would just overwrite it. So the server *asks the owner* to teleport itself, and the owner's NetworkTransform then tells everyone else.

**Connects to:** `NetworkManager` (the connect event and player objects) and `PlayerMovementController` (the teleport RPC).

---

### 5.4 `PlayerMovementController`: walking

File: `Assets/_Game/Scripts/Player/PlayerMovementController.cs`
Lives on: the Player root. A NetworkBehaviour. Moves the player **only on the owner's copy**.

This script started as Unity's Multiplayer Center sample, which is why it's in the `Unity.Multiplayer.GettingStarted.PlayerMovement` namespace. It has been changed so movement follows the direction you face.

**How it works:**
1. `OnNetworkPostSpawn()` runs right after spawning:
   - It finds the `CharacterController`, the `Move` input action, and the `NetworkTransform`.
   - If `_networkTransform.CanCommitToTransform` is true, it registers for Netcode's per-frame update. That's true only for the copy allowed to change the transform: the owner's copy, because authority is Owner. Every other copy never registers, so it never reads input and never moves itself; its position comes from the NetworkTransform instead.
2. `NetworkUpdate(...)` runs once per frame on the owner's copy only, and calls `CharacterController.Move(GetPlayerMovement())`.
3. `GetPlayerMovement()`:
   - Reads `Move` as `(x, y)`.
   - Builds a world direction from the player's own axes: `transform.right * x + transform.forward * y`. Pressing W therefore always means "forward from where I'm looking". `PlayerCameraController` is what turns the player.
   - Multiplies by `Speed * Time.deltaTime`. Speed is 5 m/s, and `deltaTime` is the frame's length in seconds, so the speed is the same at any frame rate.
4. `OnNetworkPreDespawn()` unregisters from the per-frame update.
5. `TeleportToSpawnRpc(position, rotation)` is `[Rpc(SendTo.Owner)]`, so it runs on the owner's computer. It briefly disables the CharacterController (which would otherwise fight the position change), sets the position and rotation, and re-enables it.

**Connects to:**
- `PlayerInput` (the Move action).
- `NetworkTransform` (decides who moves, and copies the result to everyone).
- `PlayerCameraController` (provides the facing direction).
- `PlayerSpawnManager` (calls the teleport).

**Note:** there's no gravity yet. The player keeps whatever height they spawned at. On a flat floor that's fine.

---

### 5.5 `PlayerCameraController`: your camera and mouse look

File: `Assets/_Game/Scripts/Player/PlayerCameraController.cs`
Lives on: the Player root. A NetworkBehaviour. Only does anything on the **owner's copy**.

**Purpose:**
1. Make sure each computer has **exactly one active camera**: your own.
2. Lock the mouse cursor to the game.
3. Turn the player left and right and tilt the camera up and down with the mouse.

**Why this is needed:** the Player prefab contains a camera. Without this script, every copy of every player on every computer would have an active camera and an AudioListener. You'd see through a random player's eyes, and Unity would warn about multiple audio listeners. So the camera is **inactive in the prefab**, and only the owner's copy switches it on.

**How it works:**
1. `OnNetworkSpawn()`:
   - Non-owners return immediately. Their camera stays off forever.
   - On the owner's copy:
     - It finds the scene's overview camera via `Camera.main`, remembers it in `sceneCamera`, and deactivates it. This works because only the scene camera is active and tagged `MainCamera` at that moment.
     - It activates its own `playerCamera`.
     - It gets the `Look` action from `PlayerInput`.
     - It locks and hides the cursor.
2. `Update()` runs every frame on every copy, but returns immediately unless `lookAction` was set, which only happens on the owner's copy. Then:
   - **Esc** unlocks the cursor. Clicking while it's unlocked locks it again.
   - While the cursor is unlocked, the mouse doesn't turn you, so you can use the editor.
   - It reads `Look` (mouse movement since the last frame) and multiplies it by `lookSensitivity`. This isn't multiplied by `deltaTime`, because mouse movement is already "per frame".
   - **Yaw** (left/right) rotates the whole Player: `transform.Rotate(0, look.x, 0)`. Because the root is turned, the NetworkTransform sends the new rotation to everyone. Others can't see it yet, because a capsule looks the same from every side.
   - **Pitch** (up/down) is stored in `pitch`, kept within ±85°, and applied only to the camera's local rotation. Only you can see it; it isn't sent over the network.
3. `OnNetworkDespawn()` switches the player camera off and the scene camera back on. If this was the owner's copy, it also unlocks the cursor. Remote players leaving never touch your cursor, because their copies have no `lookAction`.

**Connects to:**
- `PlayerInput` (the Look action).
- The scene `Camera` (turned off and on).
- `PlayerMovementController` (which uses the yaw this script sets).
- `NetworkTransform` (which syncs that yaw).
- `PlayerNameplate` (which will use `Camera.main` to face the camera).

---

### 5.6 `PlayerInfo`: the player's name on the network

File: `Assets/_Game/Scripts/Player/PlayerInfo.cs`
Lives on: the Player root. A NetworkBehaviour. Runs on **every copy**, with different roles depending on the copy.

**Purpose:** be the single source of truth for "what is this player called", the same on every computer. It's also a good future home for per-player stats like tasks done or paintball hits.

**Members:**
- `_playerName`, a `NetworkVariable<FixedString64Bytes>`:
  - default `""`;
  - **read: everyone**, **write: server only**. Only the server can change it, so the server gets the final say.
- `PlayerName`: a normal `string` property that returns the current name. This is what other scripts use.
- `NameChanged`: a **C# event**, not a network message. It fires *locally* on each copy whenever the name changes there. Other scripts subscribe to it to react, for example to update a nameplate.

**How it works:**
1. `OnNetworkSpawn()` runs on every copy:
   - It subscribes to `_playerName.OnValueChanged`, so later changes are passed on through `NameChanged`.
   - On the **owner's copy only**, it calls `SubmitNameRpc(LocalPlayerProfile.PlayerName)`, which sends your saved name to the server.
2. `SubmitNameRpc` is marked `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]`, so it **runs on the server**, and **only the owner** may call it. That stops one player from renaming another. On the server:
   - It cleans the name again with `LocalPlayerProfile.CleanName`. A client could have been modified, so the server never trusts input blindly.
   - If the name ends up empty, it uses `"Employee {OwnerClientId}"`, for example `Employee 0` for the host.
   - It sets `_playerName.Value`, and Netcode sends the new value to every copy on every computer.
3. `OnPlayerNameChanged(previous, new)` runs on each copy when the value arrives, and raises `NameChanged(new)`.
4. `OnNetworkDespawn()` unsubscribes.

**Players who join later:** a NetworkVariable's current value is sent along with the object when it's spawned for a new player. Someone who joins five minutes late still gets everyone's name. They do **not** get an `OnValueChanged` call for it, though, because nothing "changed" on their copy. That's why scripts must also read `PlayerName` directly when they start ([8.6](#86-reacting-only-to-changes-misses-the-starting-value)).

**On the host:** when the host's own player spawns, `SubmitNameRpc` is "sent" to the server, which is the same computer, so it simply runs right away.

**Connects to:**
- `LocalPlayerProfile` (the owner reads the name; the server cleans it).
- `PlayerNameplate` (listens to `NameChanged` and reads `PlayerName`).

---

### 5.7 `PlayerNameplate`: the name above the head (in progress)

File: `Assets/_Game/Scripts/UI/PlayerNameplate.cs`
Lives on: the `Nameplate` child of the Player. A NetworkBehaviour, so it can use `IsOwner` and `OnNetworkSpawn`. That's allowed on a child of a NetworkObject.

**Purpose:** show each *other* player's name above their head.

**Fields:**
- `_nameText`, serialized → the `Text (TMP)` object.
- `_canvas`, serialized → the `Canvas` object.
- `_canvasGroup`, `_fadeStartDistance` (10), `_fadeEndDistance` (20): planned for the distance fade. They're not used yet and not serialized.
- `_playerInfo`: the `PlayerInfo` on the Player root.

**What it does today:**
1. `OnNetworkSpawn()`:
   - **Owner's copy:** nothing yet. The intention is to hide your own nameplate.
   - **Everyone else's copy:**
     - It finds `PlayerInfo` on the parent with `GetComponentInParent<PlayerInfo>()`.
     - It calls `SetName(_playerInfo.PlayerName)`, which shows the current name right away (important for late joiners).
     - It subscribes `SetName` to `NameChanged` for future changes.
2. `SetName(name)`:
   - Shows the Canvas only if the name isn't empty, so no empty box flashes before the name arrives.
   - Puts the name into the text.
3. `OnNetworkDespawn()` unsubscribes from `NameChanged`.

**Planned, not yet written** (see [Section 9](#9-current-status-known-issues-and-next-steps)):
- **Hide your own plate:** on the owner's copy, `_canvas.enabled = false; enabled = false;`.
- **A `LateUpdate()` that:**
  - **makes the plate face your camera:** `transform.rotation = Camera.main.transform.rotation`. Copying the camera's rotation keeps the text readable and never mirrored, unlike `LookAt`;
  - **fades it with distance:** set the CanvasGroup's alpha from the distance to the camera, fully visible up to 10 m and invisible by 20 m.
- **Walls** will hide nameplates automatically. World-space UI is drawn with depth testing, so solid objects in front of it block it.

**Connects to:**
- `PlayerInfo` (name and change event).
- `PlayerCameraController` (indirectly: it decides which camera is `Camera.main`, which the planned billboard uses).

---

### 5.8 `Player.inputactions`: the controls

File: `Assets/_Game/Player.inputactions`

| Action | Type | Binding | Used by |
|---|---|---|---|
| `Move` | Value, Vector2 | W/A/S/D (2D vector composite) | `PlayerMovementController` |
| `Look` | Value, Vector2 | `<Mouse>/delta` (mouse movement per frame) | `PlayerCameraController` |

Every copy of the Player has a `PlayerInput`, but only the owner's copy *reads* the actions, so other players' copies never react to your keyboard.

---

## 6. How it all fits together: step-by-step timelines

### 6.1 Pressing "Start Host"

```
You type "Panagiotis" and click Start Host (NetworkDebugUI)
 1. LocalPlayerProfile.PlayerName = "Panagiotis"   → cleaned, saved to PlayerPrefs
 2. NetworkManager.StartHost()                     → this computer becomes server + client 0
 3. NetworkManager spawns a Player for client 0 (the host itself)
 4. On that Player, OnNetworkSpawn runs on each script:
      PlayerCameraController (IsOwner) → scene Camera off, PlayerCamera on, cursor locked
      PlayerInfo (IsOwner)             → SubmitNameRpc("Panagiotis") → runs locally (host = server)
                                         → cleaned → _playerName.Value = "Panagiotis"
      PlayerNameplate (IsOwner)        → owner branch (currently does nothing)
    and OnNetworkPostSpawn:
      PlayerMovementController         → owner can move → registers for per-frame update
 5. OnClientConnectedCallback(0) → PlayerSpawnManager → spawn point 0 % 2 = Spawn_01
      → TeleportToSpawnRpc → runs on owner (the host) → Player moved to (1, 1, 1)
 6. The menu disappears (now connected). You can walk and look around.
```

### 6.2 A client joins

```
Player 2 types "Alex" and clicks Start Client
 1. LocalPlayerProfile.PlayerName = "Alex" (on Player 2's computer)
 2. StartClient() → connects to 127.0.0.1:7777 → sends its NetworkConfig fingerprint
 3. HOST compares fingerprints. If different → "NetworkConfig mismatch" → client kicked (see 8.4).
    If equal → approved → client gets ID 1.
 4. HOST spawns a Player for client 1. Netcode sends to the client:
      - the host's existing Player (with its current name "Panagiotis")
      - the new Player for client 1
 5. On the CLIENT's computer, both Players spawn:
      Host's Player (not owner):  camera stays off · no input · nameplate reads "Panagiotis" → shows it
      Own Player (owner):         scene Camera off, own camera on · SubmitNameRpc("Alex") → to host
 6. HOST receives SubmitNameRpc("Alex") on its copy of client 1's Player
      → cleaned → _playerName.Value = "Alex" → sent to everyone
 7. HOST's computer: OnValueChanged → NameChanged("Alex") → nameplate above Alex shows "Alex"
    CLIENT's computer: same on its own copy (its own plate should be hidden)
 8. OnClientConnectedCallback(1) on HOST → PlayerSpawnManager → 1 % 2 = Spawn_02
      → TeleportToSpawnRpc → runs on the CLIENT (the owner) → moved to (-0.84, 1, 2)
      → the client's NetworkTransform tells everyone the new position
```

### 6.3 One frame of moving and looking (on your own computer)

```
Update (PlayerCameraController, owner only)
   mouse moved (dx, dy)
   → Player root rotates by dx × 0.1°   (yaw — synced to others by NetworkTransform)
   → camera pitch changes by −dy × 0.1°, clamped ±85°   (local only)

NetworkUpdate (PlayerMovementController, owner only)
   W/A/S/D = (x, y)
   → direction = right·x + forward·y   (uses the yaw just set)
   → CharacterController.Move(direction × 5 m/s × frame time)

NetworkTransform (owner copy)
   → sends the new position/rotation (30 times per second)

Everyone else's copy of you
   → receives position/rotation → smoothly blends toward it (interpolation)
```

### 6.4 The name, end to end

```
 Typing        Saving               Sending                 Deciding            Spreading          Showing
 ───────       ─────────            ─────────────────       ──────────────      ─────────────      ─────────────────
 NetworkDebugUI → LocalPlayerProfile → PlayerInfo (owner)  → PlayerInfo (server) → NetworkVariable → PlayerInfo (each copy)
 text field      PlayerPrefs          SubmitNameRpc          CleanName +           _playerName        NameChanged event
                                                             "Employee N" fallback                       ↓
                                                                                                 PlayerNameplate.SetName
                                                                                                 → TextMeshPro text
```

### 6.5 Someone leaves, or you stop Play

- **A client disconnects:** the host despawns that client's Player everywhere. On each remaining computer, that copy's `OnNetworkDespawn` runs: `PlayerInfo` and `PlayerNameplate` unsubscribe, and `PlayerCameraController` does nothing visible because that camera was never on.
- **You stop Play or disconnect:** your own Player's `OnNetworkDespawn` turns your camera off, turns the scene camera back on, and unlocks your cursor.

---

## 7. Who runs what: the "copies" table

The same setup as before, a host and one client. "Own" means the copy that belongs to the computer it's running on.

| Behaviour | Host's player on **host** (own) | Client's player on **host** | Client's player on **client** (own) | Host's player on **client** |
|---|---|---|---|---|
| Reads keyboard and moves (`PlayerMovementController`) | ✅ | ❌ | ✅ | ❌ |
| Sends position (`NetworkTransform`) | ✅ sends | ⬇️ receives | ✅ sends | ⬇️ receives |
| Camera on, cursor lock, mouse look (`PlayerCameraController`) | ✅ | ❌ | ✅ | ❌ |
| Sends its name to the server (`SubmitNameRpc`) | ✅ (runs locally) | ❌ | ✅ | ❌ |
| Writes the name NetworkVariable (server) | ✅ | ✅ (when the client's RPC arrives) | ❌ | ❌ |
| Listens for name changes (`OnValueChanged`) | ✅ | ✅ | ✅ | ✅ |
| Shows a nameplate (`PlayerNameplate`) | should hide (to do) | ✅ | should hide (to do) | ✅ |
| Gets teleported to a spawn (`TeleportToSpawnRpc` runs here) | ✅ | ❌ | ✅ | ❌ |

---

## 8. Problems we hit and what they teach

### 8.1 The name only worked for the host

**Symptom:** clients' names never appeared. The host logged `Write permissions (Owner) for this client instance is not allowed!`.

**Cause:** the NetworkVariable's write permission was **Owner**, but the code that writes it runs on the **server**. For a client's player, the server isn't the owner, so Netcode refused the write. It worked for the host's own player only because the host is both owner and server, so testing with the host alone hid the bug.

**Fix:** `NetworkVariableWritePermission.Server`.

**Lesson:** the write permission must match the computer that actually runs the assignment. Always test with at least one real client.

### 8.2 PlayerPrefs in a field initializer

**Symptom:** a runtime error, *"GetString is not allowed to be called from a MonoBehaviour constructor (or instance field initializer)…"*.

**Cause:** `private string _nameInput = LocalPlayerProfile.PlayerName;`. Field initializers run while Unity is still constructing the component, and many Unity APIs, PlayerPrefs included, aren't allowed then. It compiled fine, which is why it was easy to miss.

**Fix:** declare the field without a value and fill it in `Start()`.

### 8.3 `GUILayout.TextField(text, someString)`

**Symptom:** repeated "Unable to find style" errors, and no length limit.

**Cause:** the second argument is meant to be a maximum length (`int`). Passing a `string` instead quietly converts it into a *GUI style name*.

**Fix:** pass `LocalPlayerProfile.MaxNameLength`.

### 8.4 "NetworkConfig mismatch": clients rejected on join

**Symptom:** the client logs "Transport connection with server established… Awaiting connection approval…", then "Connection was closed by remote endpoint". The **host** logs `NetworkConfig mismatch`. Always check the host's console for the real reason.

**Cause:** the Player prefab's `GlobalObjectIdHash` (its network ID, [2.2](#22-how-this-games-multiplayer-is-shaped)) had been overwritten with wrong values. Over time it was 1814604603, 4271189704, 2706931490 and 1637778659, when the correct value is **2488557847**. The main editor and the virtual player ended up with different IDs, so their fingerprints differed. Two ways it gets overwritten:
- **Editing the Player through a copy in the scene and pressing Apply.** Netcode treats that copy as a scene object, gives it a scene-object ID, and Apply copies that ID into the prefab. That's also how the prefab got `m_InScenePlaced: 1`.
- **Saving a script while the Player is open in Prefab Mode (most likely).** Recompiling makes Netcode forget that the prefab is open in Prefab Mode, so it gives the prefab the ID of the copy Prefab Mode is editing (4271189704).

**Fix:**
1. Close Prefab Mode.
2. In `Player.prefab`, set `GlobalObjectIdHash: 2488557847` (and `m_InScenePlaced: 0`).
3. In the Multiplayer Play Mode window, untick and re-tick the virtual players so they reload.

**Prevention:**
- Edit the Player only by double-clicking it in the Project window.
- Never edit it through a copy in a scene.
- Close Prefab Mode before editing scripts or pressing Play.
- Before committing, check whether `GlobalObjectIdHash` changed in the diff.

### 8.5 Too many cameras, and the RectTransform on the root

- **Cameras:** a camera inside the Player prefab is active on every copy unless something turns it off. Fixed by keeping it inactive in the prefab and letting `PlayerCameraController` enable it for the owner only.
- **RectTransform:** at one point the Player root's Transform had turned into a `RectTransform` (a UI transform), probably from adding a UI element straight onto the root. It was changed back. UI belongs on its own child object (like `Nameplate`), never on the root of a 3D character.

### 8.6 Reacting only to changes misses the starting value

If a script only listens to `OnValueChanged` or `NameChanged`, players who join later never see existing names, because nothing "changes" for them. **Always apply the current value once in `OnNetworkSpawn`, then subscribe for future changes.** `PlayerNameplate` does this with `SetName(_playerInfo.PlayerName)`.

### 8.7 `name` vs `PlayerName`

Every Unity component has a built-in `name` property: the **GameObject's** name, for example "Player(Clone)". `PlayerInfo.PlayerName` is the player's chosen name. Mixing them up made every nameplate say "Player(Clone)". Avoid calling your own parameters `name` for the same reason.

---

## 9. Current status, known issues and next steps

### Nameplate: remaining work

| Item | Where | Why it matters |
|---|---|---|
| Hide your own plate (owner branch is empty) | `PlayerNameplate.OnNetworkSpawn` | Otherwise "New Text" floats above your own head. |
| Guard against a null `_playerInfo` in `OnNetworkDespawn` | `PlayerNameplate.OnNetworkDespawn` | On your own player `_playerInfo` is never set, so this throws a NullReferenceException every time you disconnect or stop Play. |
| `LateUpdate`: face the camera and fade with distance | `PlayerNameplate` | The plate currently turns with the player, so from behind the text looks mirrored. |
| `[SerializeField]` on `_canvasGroup` and the fade distances, plus a CanvasGroup on the Canvas | script + prefab | The fade needs the CanvasGroup, and serializing lets you tune distances in the Inspector. |
| Canvas scale 0.01 and Image scale 1 (instead of 0.1 on both) | prefab | Clearer; the result is the same overall size. |
| Horizontal Layout Group and Content Size Fitter on the Image, pivot (0.5, 0) | prefab | Makes the box fit the name's length and grow upward from the head. |
| Clear the default "New Text" and untick Raycast Target on the Text | prefab | Cosmetic and tidiness. |
| `m_InScenePlaced: 0` | `Player.prefab` | Left over from the scene-copy editing; a spawned player isn't an in-scene object. |

### Other known issues

- **No gravity:** players keep their spawn height. It's fine on the flat test floor, but stairs and drops won't work.
- **Only two spawn points:** a third player (ID 2) spawns on top of the host (ID 0).
- **Turning isn't visible to others:** rotation *is* synced, but a capsule looks the same from every side. Adding a "nose" or face mesh would show it.
- **Up/down look isn't synced:** only your camera tilts, so others can't tell where you look vertically.
- **Local network only:** the transport listens on `127.0.0.1`. Playing with friends on other computers needs a different address or Unity's Multiplayer Services (Relay/Sessions), which `CONVENTIONS.md` already plans for.
- **Duplicate class name:** the Multiplayer Center quickstart package has its own `PlayerMovementController` with the same full name, which causes a CS0436 compiler warning. The prefab uses the project's copy. Fix by moving the project's copy into its own namespace, or by removing the quickstart package.
- **Build settings:** `Testing.unity` isn't in the build's scene list, which still contains the template's `SampleScene` and a package example scene. That matters as soon as you make a standalone build.
- **Fallback name starts at 0:** the host's fallback is `Employee 0`. Use `OwnerClientId + 1` if you'd prefer it to start at 1.

### Suggested next steps

1. **Finish the nameplate** (the table above).
2. **Interaction system ("press E on things"):** the next foundation the GDD needs. Computers, food, the paintball gun, cleaning tools and the manager's checks all build on it.
3. **The core loop skeleton:** shift timer, hidden productivity value, one computer task, vague announcements, end-of-day report.

---

## 10. File map

| File | Type | What it is |
|---|---|---|
| [`Assets/_Game/Scripts/Networking/NetworkDebugUI.cs`](../Assets/_Game/Scripts/Networking/NetworkDebugUI.cs) | MonoBehaviour | Name field + Start Host / Start Client buttons |
| [`Assets/_Game/Scripts/Core/LocalPlayerProfile.cs`](../Assets/_Game/Scripts/Core/LocalPlayerProfile.cs) | static class | Saves and cleans your name (PlayerPrefs) |
| [`Assets/_Game/Scripts/Core/PlayerSpawnManager.cs`](../Assets/_Game/Scripts/Core/PlayerSpawnManager.cs) | MonoBehaviour | Moves new players to spawn points (server) |
| [`Assets/_Game/Scripts/Player/PlayerMovementController.cs`](../Assets/_Game/Scripts/Player/PlayerMovementController.cs) | NetworkBehaviour | WASD movement relative to facing (owner) + teleport RPC |
| [`Assets/_Game/Scripts/Player/PlayerCameraController.cs`](../Assets/_Game/Scripts/Player/PlayerCameraController.cs) | NetworkBehaviour | Own camera, cursor lock, mouse look (owner) |
| [`Assets/_Game/Scripts/Player/PlayerInfo.cs`](../Assets/_Game/Scripts/Player/PlayerInfo.cs) | NetworkBehaviour | Player name as a server-written NetworkVariable |
| [`Assets/_Game/Scripts/UI/PlayerNameplate.cs`](../Assets/_Game/Scripts/UI/PlayerNameplate.cs) | NetworkBehaviour | Shows other players' names (in progress) |
| [`Assets/_Game/Player.inputactions`](../Assets/_Game/Player.inputactions) | Input Actions asset | `Move` (WASD) and `Look` (mouse) |
| [`Assets/_Game/Prefabs/Player/Player.prefab`](../Assets/_Game/Prefabs/Player/Player.prefab) | Prefab | The player: body, camera, nameplate, all player scripts |
| [`Assets/_Game/Scenes/Test/Testing.unity`](../Assets/_Game/Scenes/Test/Testing.unity) | Scene | NetworkManager, spawn points, menu, camera, ground |
| [`CONVENTIONS.md`](../CONVENTIONS.md) | Notes | Naming, units and the networking stack choices |
| [`GDD.docx`](../GDD.docx) | Design document | The game design these systems are building toward |
