namespace SSMP.Api.Eventing; 

/// <summary>
/// Represents an event that can be cancelled.
/// </summary>
public interface ICancellable {
    /// <summary>
    /// The cancellation state of the event. A cancelled event will not execute on the server.
    /// </summary>
    public bool Cancelled { get; set; }
}
