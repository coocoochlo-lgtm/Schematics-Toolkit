using System;

namespace Remedy.Schematics
{
    /// <summary>
    /// Something the Schematics toolkit identifies by its own permanent id: Schematic Graphs and Signals. The id is
    /// saved with the asset, so it stays the same across sessions, renames and moves, and works the same in the editor
    /// and in builds. (Schematic Objects have their <see cref="SchematicObject.ObjectID"/>.)
    /// </summary>
    public interface ISchematicAsset
    {
        /// <summary>The asset's permanent id: 32 hex characters, never empty.</summary>
        string Uid { get; }

        /// <summary>Gives the asset a new id. The editor does this when an asset is duplicated (the copy had the same id).</summary>
        void RegenerateUid();
    }

    /// <summary>Making Schematic ids.</summary>
    public static class SchematicUid
    {
        public static string New() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// The id stored in <paramref name="field"/>, made the first time it's needed. A new id is reported through
        /// <see cref="Assigned"/>, so the editor saves it with the asset (and it never changes after that).
        /// </summary>
        public static string Ensure(ref string field, UnityEngine.Object owner)
        {
            if (string.IsNullOrEmpty(field))
            {
                field = New();
                Assigned?.Invoke(owner);
            }
            return field;
        }

        /// <summary>An asset was given its id. The editor saves it.</summary>
        public static Action<UnityEngine.Object> Assigned;

        /// <summary>The id of a Schematic asset, or null for anything else.</summary>
        public static string Of(UnityEngine.Object asset) => asset is ISchematicAsset identified && asset != null ? identified.Uid : null;
    }
}
