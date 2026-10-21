using UnityEditor;

namespace Remedy.Audio
{
    // Graph Variables can hold a playing Sound Cue (a Play Sound Cue's Player): keep it to change its Parameters later,
    // or stop it.
    [InitializeOnLoad]
    internal static class SoundCueVariableTypes
    {
        static SoundCueVariableTypes() => GraphScopeEditing.AddVariableType("Objects/Sound Cue Player", typeof(SoundCuePlayer));
    }
}
