//using SaintsField;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ScriptableObjectWithID<T> : ScriptableObject where T : ScriptableObjectWithID<T>
{
    private static Dictionary<int, T> _lookup = new Dictionary<int, T>();

    // Serialized index for persistence
    //[SerializeField, ReadOnly]
    private int _id = -1;
    public int ID => _id;

    private static int _idCounter = 0;

    public static Dictionary<int, T> Lookup
    {
        get
        {
            if (_lookup.Count == 0)
            {
                _idCounter = 0;

                var objs = Resources.LoadAll<T>("");
                // Clear old lookup to avoid duplication on domain reload
                _lookup.Clear();

                // Assign stable indices based on loaded order
                for (int i = 0; i < objs.Length; i++)
                {
                    var obj = objs[i];
                    if (obj._id != i)
                    {
                        obj._id = i;
                        _idCounter++;

#if UNITY_EDITOR
                        UnityEditor.EditorUtility.SetDirty(obj);
                        UnityEditor.AssetDatabase.SaveAssets();
#endif
                    }
                    if (!_lookup.ContainsKey(i))
                    {
                        _lookup[i] = obj;
                    }
                }
            }
            return _lookup;
        }
    }

    private void OnEnable()
    {
        if(_id == -1)
            _id = _idCounter++;
    }
}
