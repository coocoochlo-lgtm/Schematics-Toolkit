using Remedy.Framework;

/// <summary>
/// Project-wide settings that runtime code reads without needing the Game Data asset (which lives in an assembly that
/// depends on this one). Game Data pushes its values here when it loads.
/// </summary>
public class GlobalSubsystem
{
    private static IntTag _playerTag = IntTags.Player;

    /// <summary>
    /// The Int Tag that marks the player: Trigger Volumes only react to objects with it, and vehicles only listen to
    /// player input while a driver with it is aboard. <see cref="IntTags.Player"/> unless Game Data says otherwise.
    /// </summary>
    public static IntTag PlayerTag => _playerTag;

    /// <summary>Sets the Player Tag (Game Data does this when it loads).</summary>
    public static void SetPlayerTag(IntTag tag)
    {
        _playerTag = tag;
    }
}
