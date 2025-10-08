using HutongGames.PlayMaker;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace SSMP.Game.Client.Entity.Action; 

/// <summary>
/// A hooked FSM action for an entity.
/// </summary>
internal class HookedEntityAction {
    /// <summary>
    /// The instance of the action that was hooked.
    /// </summary>
    public FsmStateAction Action { get; set; }
    
    /// <summary>
    /// The index of the FSM in which the action was hooked.
    /// </summary>
    public int FsmIndex { get; set; }
    
    /// <summary>
    /// The index of the state in which the action was hooked.
    /// </summary>
    public int StateIndex { get; set; }

    /// <summary>
    /// The index of the hooked action.
    /// </summary>
    public int ActionIndex { get; set; }
}
