using System;

/// <summary>
/// Documents a node, Schematic Behavior, Variable or Signal for the assistant (and for Sprocket's "What is this?"),
/// as JSON. Every documented piece is added to the assistant's API reference when the project recompiles, so the
/// language model knows what exists and what it's good for when it composes gameplay logic.
/// <para>
/// Fields: <c>name</c>, <c>kind</c> (node, behavior, variable, signal), <c>description</c>, and whatever else helps:
/// <c>flow</c> (event, action, query, condition), <c>inputs</c>/<c>outputs</c> (nodes), <c>params</c> and
/// <c>direction</c> (signals), <c>type</c> and <c>default</c> (variables), <c>use_for</c> (gameplay uses), <c>notes</c>.
/// </para>
/// <code>[SchematicDoc(@"{""name"": ""Wait"", ""kind"": ""node"", ""description"": ""Pauses the flow..."", ""use_for"": [""delays""]}")]</code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field, Inherited = false)]
public sealed class SchematicDocAttribute : Attribute
{
    public readonly string Json;

    public SchematicDocAttribute(string json) { Json = json; }
}
