#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
namespace SSMP.Game.Client.Entity; 

/// <summary>
/// Simple generic class for a pair of objects that are shared by the client and host.
/// </summary>
/// <typeparam name="T">The type of the objects.</typeparam>
internal class HostClientPair<T> {
    /// <summary>
    /// The client object.
    /// </summary>
    public T Client { get; set; }
    /// <summary>
    /// The host object.
    /// </summary>
    public T Host { get; set; }
}
