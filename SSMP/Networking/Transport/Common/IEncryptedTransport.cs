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
