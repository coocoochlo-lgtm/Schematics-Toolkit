using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the int ids Unity still passes to some editor callbacks (OnOpenAsset, hierarchy row drawing) back into objects.
/// EditorUtility.InstanceIDToObject is a compile error in newer Unity 6 versions (replaced by EntityId) but still
/// exists, so it's reached by reflection: this compiles and works in every version.
/// </summary>
public static class EditorObjectIds
{
    private static MethodInfo _method;
    private static bool _searched;
    private static readonly object[] _argument = new object[1];

    public static Object ToObject(int instanceId)
    {
        if (!_searched)
        {
            _searched = true;
            _method = typeof(EditorUtility).GetMethod("InstanceIDToObject", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (_method == null)
                Debug.LogWarning("EditorUtility.InstanceIDToObject no longer exists in this Unity version; update EditorObjectIds to use EntityId.");
        }
        if (_method == null)
            return null;
        _argument[0] = instanceId;
        return _method.Invoke(null, _argument) as Object;
    }
}
