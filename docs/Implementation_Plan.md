# Networking Implementation Plan - Steam P2P & UDP Hole Punching

## Executive Summary

**Goal:** Implement a robust, multi-transport networking system supporting:
1.  **Direct UDP** (LAN/Direct IP)
2.  **Steam P2P** (Steam Relays/NAT Traversal)
3.  **UDP Hole Punching** (NAT Traversal via Internet Master Server)

**Key Insight:** All three methods provide a stream of bytes. We abstract this at the **encrypted transport level**.

**Architecture:**
```
┌─────────────────────────────────────────┐
│      NetClient / NetServer              │  ← Application Layer
│  (packet management, game logic)        │
└──────────────┬──────────────────────────┘
               │ uses IEncryptedTransport
       ┌───────┴───────────────┬──────────────────┐
       │                       │                  │
  ┌────▼─────┐           ┌─────▼─────┐      ┌─────▼─────┐
  │ Direct   │           │  Steam    │      │ HolePunch │  ← Encrypted Transport Layer
  │ UDP+DTLS │           │   P2P     │      │ UDP+DTLS  │
  └────┬─────┘           └─────┬─────┘      └─────┬─────┘
       │                       │                  │
  ┌────▼─────┐           ┌─────▼─────┐      ┌─────▼─────┐
  │UDP Socket│           │Steam API  │      │UDP Socket │  ← Raw Transport Layer
  └──────────┘           └───────────┘      │+Master Svr│
                                            └───────────┘
```

---

## Part 1: Core Interfaces

### 1.1 IEncryptedTransport (Client-Side)

**Purpose:** Abstract encrypted connection for clients.

**File:** `Networking/Transport/Common/IEncryptedTransport.cs`

```csharp
namespace SSMP.Networking.Transport.Common;

internal interface IEncryptedTransport {
    event Action<byte[], int>? DataReceivedEvent;
    
    /// <summary>
    /// Connect to remote peer.
    /// Address format depends on implementation:
    /// - UDP: "127.0.0.1"
    /// - Steam: "76561198..." (SteamID)
    /// - HolePunch: "LobbyID" or "PeerID" (resolved via Master Server)
    /// </summary>
    void Connect(string address, int port);
    
    int Send(byte[] buffer, int offset, int length);
    int Receive(byte[] buffer, int offset, int length, int waitMillis);
    void Disconnect();
}
```

### 1.2 IEncryptedTransportServer (Server-Side)

**Purpose:** Abstract encrypted server that accepts multiple clients.

**File:** `Networking/Transport/Common/IEncryptedTransportServer.cs`

```csharp
namespace SSMP.Networking.Transport.Common;

internal interface IEncryptedTransportServer {
    event Action<IEncryptedTransportClient>? ClientConnectedEvent;
    
    /// <summary>
    /// Start listening.
    /// - UDP: Binds to port
    /// - Steam: Opens channel
    /// - HolePunch: Registers with Master Server
    /// </summary>
    void Start(int port);
    
    void Stop();
    void DisconnectClient(IEncryptedTransportClient client);
}

internal interface IEncryptedTransportClient {
    string ClientIdentifier { get; }
    event Action<byte[], int>? DataReceivedEvent;
    int Send(byte[] buffer, int offset, int length);
}
```

---

## Part 2: Direct UDP Implementation (Wraps Existing DTLS)

### 2.1 UdpEncryptedTransport

**Purpose:** Wrap `DtlsClient` to provide `IEncryptedTransport`.

**File:** `Networking/Transport/UDP/UdpEncryptedTransport.cs`

*Implementation details: Wraps `DtlsClient`. `Connect` takes IP string.*

### 2.2 UdpEncryptedTransportServer

**Purpose:** Wrap `DtlsServer` to provide `IEncryptedTransportServer`.

**File:** `Networking/Transport/UDP/UdpEncryptedTransportServer.cs`

*Implementation details: Wraps `DtlsServer`. `Start` binds to local port.*

---

## Part 3: Steam P2P Implementation

### 3.1 SteamEncryptedTransport

**Purpose:** Steam P2P client transport.

**File:** `Networking/Transport/Steam/SteamEncryptedTransport.cs`

*Implementation details: Uses `SteamNetworking`. `Connect` takes SteamID string.*

### 3.2 SteamEncryptedTransportServer

**Purpose:** Steam P2P server.

**File:** `Networking/Transport/Steam/SteamEncryptedTransportServer.cs`

*Implementation details: Uses `SteamNetworking`. `Start` opens P2P channel.*

---

## Part 4: UDP Hole Punching Implementation

### 4.1 Concept
Uses a central **Master Server** (Internet Server) to facilitate NAT traversal.
1.  **Server** registers with Master Server, gets a `LobbyID`.
2.  **Client** asks Master Server for `LobbyID` info.
3.  **Master Server** tells both Client and Server each other's Public IP:Port.
4.  **Both** send "Punch" packets to each other.
5.  **Connection** established (DTLS handshake follows).

### 4.2 HolePunchEncryptedTransport (Client)

**File:** `Networking/Transport/HolePunch/HolePunchEncryptedTransport.cs`

```csharp
internal class HolePunchEncryptedTransport : IEncryptedTransport {
    private DtlsClient _dtlsClient;
    private string _masterServerAddress;
    
    public HolePunchEncryptedTransport(string masterServerAddress) {
        _masterServerAddress = masterServerAddress;
        _dtlsClient = new DtlsClient();
        // ...
    }
    
    public void Connect(string address, int port) {
        // address is likely a LobbyID here
        var lobbyId = address;
        
        // 1. Contact Master Server
        var peerEndpoint = ResolvePeerViaMasterServer(lobbyId);
        
        // 2. Perform Hole Punching
        PunchHole(peerEndpoint);
        
        // 3. Connect DTLS
        _dtlsClient.Connect(peerEndpoint.Address.ToString(), peerEndpoint.Port);
    }
    
    // ... Send/Receive delegate to _dtlsClient
}
```

### 4.3 HolePunchEncryptedTransportServer (Server)

**File:** `Networking/Transport/HolePunch/HolePunchEncryptedTransportServer.cs`

```csharp
internal class HolePunchEncryptedTransportServer : IEncryptedTransportServer {
    private DtlsServer _dtlsServer;
    private string _masterServerAddress;
    
    public void Start(int port) {
        _dtlsServer.Start(port);
        
        // Register with Master Server
        RegisterWithMasterServer(port);
    }
    
    // ... Client management delegates to _dtlsServer
}
```

---

## Part 5: Implementation Order

1.  **Interfaces** (Branch 1)
    *   Define `IEncryptedTransport`, `IEncryptedTransportServer`.
2.  **UDP Wrappers** (Branch 2)
    *   Implement `UdpEncryptedTransport` (Direct IP).
3.  **Refactor Core** (Branches 3 & 4)
    *   Update `NetClient`/`NetServer` to use interfaces.
4.  **Steam P2P** (Branch 5)
    *   Implement Steam transport.
5.  **Hole Punching** (Branch 6 - NEW)
    *   Implement `HolePunchEncryptedTransport`.
    *   (Requires Master Server implementation - out of scope for this doc, but transport assumes it exists).

---

## Part 6: Verification

### UDP Direct
*   Connect via `127.0.0.1`.

### Steam P2P
*   Connect via SteamID.

### Hole Punching
*   Start Master Server (mock or real).
*   Server registers.
*   Client connects via LobbyID.
*   Verify NAT traversal works.
