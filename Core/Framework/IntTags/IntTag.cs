using System;

namespace Remedy.Framework
{
    /// <summary>
    /// A tag that is really just a number. In the Inspector (and in Schematic graphs) it shows as a dropdown of names,
    /// but what gets saved and compared is the int, so renaming a tag never breaks anything that uses it, and looking
    /// objects up by tag is a dictionary hit instead of a string compare.
    /// </summary>
    /// <remarks>
    /// The built-in tags live in <see cref="IntTags"/> (Player, LevelDesign, ...). Your own tags are added from the
    /// dropdown's "Add Tag..." item and stored in the IntTagsData asset. Converts to and from int freely, so
    /// <c>IntTag tag = IntTags.Player;</c> and <c>int id = tag;</c> both work.
    /// </remarks>
    [Serializable]
    public struct IntTag : IEquatable<IntTag>
    {
        /// <summary>The tag's id: the key it is stored under in <see cref="IntTagRegistry"/>.</summary>
        public int Value;

        public IntTag(int v) { Value = v; }

        /// <summary>True for every tag except Untagged (0).</summary>
        public bool IsTagged => Value != IntTags.Untagged;

        /// <summary>The tag's display name (e.g. "Player"); see <see cref="IntTags.NameOf"/>.</summary>
        public string Name => IntTags.NameOf(Value);

        public static implicit operator int(IntTag tag) => tag.Value;
        public static implicit operator IntTag(int value) => new IntTag(value);

        public static bool operator ==(IntTag a, IntTag b) => a.Value == b.Value;
        public static bool operator !=(IntTag a, IntTag b) => a.Value != b.Value;

        public bool Equals(IntTag other) => Value == other.Value;
        public override bool Equals(object obj) => obj is IntTag other && other.Value == Value;
        public override int GetHashCode() => Value;

        /// <summary>The tag's name, so logs and debuggers read "Player" rather than "1".</summary>
        public override string ToString() => IntTags.NameOf(Value);
    }
}
