using System;
using SSMP.Api.Eventing;
using SSMP.Api.Eventing.ServerEvents;
using SSMP.Api.Server;
using SSMP.Networking.Packet.Data;

namespace SSMP.Eventing.ServerEvents;

/// <inheritdoc cref="IPlayerChatEvent" />
internal class PlayerChatEvent : ServerEvent, IPlayerChatEvent {
    /// <inheritdoc />
    public bool Cancelled { get; set; }
    
    /// <inheritdoc />
    public IServerPlayer Player { get; }

    private string _message;
    
    /// <inheritdoc />
    public string Message {
        get => _message;
        set {
            if (value == null) {
                throw new ArgumentNullException(nameof(value), "Message cannot be null");
            }

            if (value.Length > ChatMessage.MaxMessageLength) {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Message cannot be longer than {ChatMessage.MaxMessageLength} characters");
            }

            _message = value;
        }
    }

    /// <inheritdoc />
    public PlayerChatEvent(IServerPlayer player, string message) {
        Player = player;
        Message = message;
    }
}
