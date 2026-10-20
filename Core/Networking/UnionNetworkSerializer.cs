#if SCHEMATICS_NETCODE
using Remedy.Framework;
using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace Remedy.Networking
{
    /// <summary>
    /// Packs graph values (Unions) for sending to other peers. Numbers, vectors, colours, rotations, bools and strings
    /// travel as they are; GameObjects and components travel as their NetworkObject plus the path below it, so they
    /// arrive as the matching object on the other peer. Anything else arrives as null.
    /// </summary>
    public static class UnionNetworkSerializer
    {
        private enum Kind : byte { Null, Vector, String, GameObject, Component }

        public static byte[] Pack(string nodeId, string[] ports, Union[] values)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(nodeId ?? "");
            int count = ports != null ? Math.Min(ports.Length, values.Length) : 0;
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(ports[i] ?? "");
                Write(writer, values[i]);
            }
            return stream.ToArray();
        }

        public static void Unpack(byte[] payload, out string nodeId, out string[] ports, out Union[] values)
        {
            using var reader = new BinaryReader(new MemoryStream(payload));
            nodeId = reader.ReadString();
            int count = reader.ReadInt32();
            ports = new string[count];
            values = new Union[count];
            for (int i = 0; i < count; i++)
            {
                ports[i] = reader.ReadString();
                values[i] = Read(reader);
            }
        }

        /// <summary>Writes one value (see the class summary for what survives the trip).</summary>
        public static void WriteUnion(BinaryWriter writer, Union value) => Write(writer, value);

        /// <summary>Reads one value written by <see cref="WriteUnion"/>.</summary>
        public static Union ReadUnion(BinaryReader reader) => Read(reader);

        /// <summary>
        /// Whether a value arrives as itself on other peers: false for objects that aren't part of a spawned
        /// NetworkObject (they'd arrive as null) and for types the network can't carry.
        /// </summary>
        public static bool CanSend(Union value)
        {
            switch (value.Type)
            {
                case Union.ValueType.GameObject:
                case Union.ValueType.Transform:
                case Union.ValueType.Component:
                    var obj = value.GetValue<UnityEngine.Object>();
                    if (obj == null)
                        return true; // null stays null
                    var gameObject = obj is GameObject go ? go : obj is Component c ? c.gameObject : null;
                    var networkObject = gameObject != null ? gameObject.GetComponentInParent<NetworkObject>() : null;
                    return networkObject != null && networkObject.IsSpawned;
                case Union.ValueType.Bool:
                case Union.ValueType.Int:
                case Union.ValueType.Float:
                case Union.ValueType.Vector2:
                case Union.ValueType.Vector3:
                case Union.ValueType.Vector4:
                case Union.ValueType.Color:
                case Union.ValueType.Color32:
                case Union.ValueType.Quaternion:
                case Union.ValueType.LayerMask:
                case Union.ValueType.String:
                case Union.ValueType.Null:
                    return true;
                case Union.ValueType.Material:
                case Union.ValueType.Texture:
                case Union.ValueType.AudioClip:
                case Union.ValueType.PhysicsMaterial:
                    // Assets don't travel (yet); only an empty one does.
                    return value.GetValue<UnityEngine.Object>() == null;
                default:
                    // Lists, arrays, scenes and other values (enums, structs...) don't travel; they'd arrive as null.
                    return false;
            }
        }

        private static void Write(BinaryWriter writer, Union value)
        {
            switch (value.Type)
            {
                case Union.ValueType.Bool:
                case Union.ValueType.Int:
                case Union.ValueType.Float:
                case Union.ValueType.Vector2:
                case Union.ValueType.Vector3:
                case Union.ValueType.Vector4:
                case Union.ValueType.Color:
                case Union.ValueType.Color32:
                case Union.ValueType.Quaternion:
                case Union.ValueType.LayerMask:
                    // All stored as a Vector4.
                    var v = value.GetValue<Vector4>();
                    writer.Write((byte)Kind.Vector);
                    writer.Write((byte)value.Type);
                    writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); writer.Write(v.w);
                    return;
                case Union.ValueType.String:
                    writer.Write((byte)Kind.String);
                    writer.Write(value.GetValue<string>() ?? "");
                    return;
                case Union.ValueType.GameObject:
                case Union.ValueType.Transform:
                case Union.ValueType.Component:
                    var obj = value.GetValue<UnityEngine.Object>();
                    if (WriteObject(writer, obj))
                        return;
                    break;
            }
            writer.Write((byte)Kind.Null);
        }

        private static bool WriteObject(BinaryWriter writer, UnityEngine.Object obj)
        {
            var gameObject = obj is GameObject go ? go : obj is Component c ? c.gameObject : null;
            if (gameObject == null)
                return false;
            var networkObject = gameObject.GetComponentInParent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned)
                return false;

            writer.Write((byte)(obj is GameObject ? Kind.GameObject : Kind.Component));
            writer.Write(networkObject.NetworkObjectId);
            writer.Write(PathBelow(networkObject.transform, gameObject.transform));
            if (obj is Component component)
                writer.Write(component.GetType().AssemblyQualifiedName ?? "");
            return true;
        }

        private static Union Read(BinaryReader reader)
        {
            var kind = (Kind)reader.ReadByte();
            switch (kind)
            {
                case Kind.Vector:
                    var type = (Union.ValueType)reader.ReadByte();
                    var v = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var union = new Union(v);
                    union.Type = type;
                    return union;
                case Kind.String:
                    return new Union(reader.ReadString());
                case Kind.GameObject:
                case Kind.Component:
                    ulong id = reader.ReadUInt64();
                    string path = reader.ReadString();
                    string componentType = kind == Kind.Component ? reader.ReadString() : null;
                    var target = Resolve(id, path);
                    if (target == null)
                        return Union.Null;
                    if (componentType == null)
                        return new Union(target);
                    var resolvedType = Type.GetType(componentType);
                    var component = resolvedType != null ? target.GetComponent(resolvedType) : null;
                    return component != null ? new Union(component) : Union.Null;
            }
            return Union.Null;
        }

        private static GameObject Resolve(ulong networkObjectId, string path)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null || !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var networkObject))
                return null;
            var transform = string.IsNullOrEmpty(path) ? networkObject.transform : networkObject.transform.Find(path);
            return transform != null ? transform.gameObject : null;
        }

        private static string PathBelow(Transform root, Transform target)
        {
            if (target == root)
                return "";
            string path = target.name;
            for (var parent = target.parent; parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}
#endif
