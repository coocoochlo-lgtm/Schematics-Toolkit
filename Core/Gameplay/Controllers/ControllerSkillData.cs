using UnityEngine;

/// <summary>
/// What an AI Controller has learned, kept as an asset: give it to Create AI Controller and the AI starts out knowing
/// it (and, in the editor, keeps adding to it as it learns). The learning system (Remedy ManifoldNet) makes the real
/// kind: Learned Controls (Create → Remedy Toolkit → AI → Learned Controls).
/// </summary>
public abstract class ControllerSkillData : ScriptableObject
{
}
