# Networking Branching Strategy

## Branch Structure

```
main
 └── feature/encrypted-transport-abstraction
      ├── feature/encrypted-transport-interfaces        [Branch 1]
      ├── feature/udp-encrypted-transport-wrappers      [Branch 2]
      ├── feature/netclient-transport-refactor          [Branch 3]
      ├── feature/netserver-transport-refactor          [Branch 4]
      ├── feature/steam-p2p-implementation              [Branch 5]
      └── feature/udp-hole-punching                     [Branch 6]
```

---

## Branch 1: Encrypted Transport Interfaces
**Goal:** Define `IEncryptedTransport` interfaces.
**Base:** `main`

## Branch 2: UDP Encrypted Transport Wrappers
**Goal:** Wrap `DtlsClient`/`DtlsServer` for Direct UDP.
**Base:** `feature/encrypted-transport-abstraction`

## Branch 3: NetClient Transport Refactor
**Goal:** Update `NetClient` to use `IEncryptedTransport`.
**Base:** `feature/encrypted-transport-abstraction`

## Branch 4: NetServer Transport Refactor
**Goal:** Update `NetServer` to use `IEncryptedTransportServer`.
**Base:** `feature/encrypted-transport-abstraction`

## Branch 5: Steam P2P Implementation
**Goal:** Implement Steam P2P transport.
**Base:** `feature/encrypted-transport-abstraction`

---

## Branch 6: UDP Hole Punching Implementation (NEW)

**Branch Name:** `feature/udp-hole-punching`

**Base:** `feature/encrypted-transport-abstraction` (after Branch 4 merged)

**Goal:** Add UDP Hole Punching transport using an Internet Master Server.

**Files to Create:**
- `Networking/Transport/HolePunch/HolePunchEncryptedTransport.cs`
- `Networking/Transport/HolePunch/HolePunchEncryptedTransportServer.cs`

**Dependencies:**
- Requires a Master Server (external or self-hosted) protocol.

**Changes:**
- Implement `IEncryptedTransport` using UDP hole punching logic.
- Connects to Master Server to resolve peers.
- Performs hole punching dance.
- Hands off to DTLS once connected.

**PR Description:**
```
# Add UDP Hole Punching Transport

Implements NAT traversal via a Master Server for non-Steam users.

## Components
- `HolePunchEncryptedTransport`: Client that punches holes to server.
- `HolePunchEncryptedTransportServer`: Server that registers with Master Server.

## Usage
```csharp
var transport = new HolePunchEncryptedTransport("master.server.com");
client.Connect("LobbyID", 0);
```
```

**Merge to:** `feature/encrypted-transport-abstraction`

---

## Final Merge Strategy

Once all branches are merged to `feature/encrypted-transport-abstraction`:

```bash
git checkout main
git merge feature/encrypted-transport-abstraction
```
