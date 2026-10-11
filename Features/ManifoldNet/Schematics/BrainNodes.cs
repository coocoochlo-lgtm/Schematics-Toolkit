using BlueGraph;
using Remedy.Box3DPhysics;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Schematics
{
    /// <summary>Marks a node field as the name of one of the Brain's slots (the editor shows a dropdown).</summary>
    public sealed class BrainSlotAttribute : PropertyAttribute
    {
        public readonly bool Output;
        public BrainSlotAttribute(bool output) { Output = output; }
    }

    /// <summary>Base for nodes that act on a brain: the running object's, or the one connected to Brain Of.</summary>
    [Serializable]
    public abstract class BrainNodeBase : SchematicActionNode
    {
        [Input("Brain Of")] public GameObject BrainOf;

        protected BrainAgent Agent(GameObject instance)
        {
            var target = GetInputValue<GameObject>("Brain Of", BrainOf);
            if (target == null)
                target = instance;
            var agent = Brain.AgentOn(target);
            if (agent == null)
                throw new InvalidOperationException($"{(target != null ? target.name : "This object")} has no running Brain (is a Brain Definition set?).");
            return agent;
        }
    }

    /// <summary>Feeds one input slot. Use it on On Think. A slot of Size n takes the Values list (or Value for one).</summary>
    [SchematicDoc(@"{""name"": ""Set Brain Input"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""Feeds one input slot. Use it on On Think. A slot of Size n takes the Values list (or Value for one)."", ""inputs"": [{""name"": ""Value"", ""type"": ""float""}, {""name"": ""Values"", ""type"": ""float[]""}, {""name"": ""Brain Of"", ""type"": ""GameObject""}], ""use_for"": [""giving an AI what it senses (distances, directions, health)""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Use on the Brain's On Think Signal. Slot is a name from the Brain's IO Dock section.""}")]
    [Serializable]
    [Node(Name = "Set Brain Input", Path = "AI/Brain"), Tags("Object")]
    public class SetBrainInputNode : BrainNodeBase
    {
        [Editable, BrainSlot(false)] public string Slot = "Input";
        [Input("Value")] public float Value;
        [Input("Values")] public float[] Values;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var agent = Agent(instance);
            var values = GetInputValue<float[]>("Values", null);
            if (values != null && values.Length > 0)
                agent.SetInput(Slot, values);
            else
                agent.SetInput(Slot, GetInputValue("Value", Value));
        }
    }

    /// <summary>Reads one output slot. Use it on On Decided.</summary>
    [SchematicDoc(@"{""name"": ""Get Brain Output"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""Reads one output slot. Use it on On Decided."", ""inputs"": [{""name"": ""Brain Of"", ""type"": ""GameObject""}], ""outputs"": [{""name"": ""Value"", ""type"": ""float""}, {""name"": ""Pressed"", ""type"": ""bool""}, {""name"": ""Choice"", ""type"": ""int""}, {""name"": ""Option"", ""type"": ""string""}], ""use_for"": [""reading what an AI decided (move, jump, attack, which weapon)""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Use on the Brain's On Decided Signal.""}")]
    [Serializable]
    [Node(Name = "Get Brain Output", Path = "AI/Brain"), Tags("Object")]
    public class GetBrainOutputNode : BrainNodeBase
    {
        [Editable, BrainSlot(true)] public string Slot = "Output";

        [Output("Value")] public float Value;
        [Output("Pressed")] public bool Pressed;
        [Output("Choice")] public int Choice;
        [Output("Option")] public string Option;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var agent = Agent(instance);
            int index = agent.Definition.FindOutput(Slot);
            if (index < 0)
                throw new InvalidOperationException($"The brain has no output \"{Slot}\".");
            var kind = agent.Definition.Outputs[index].Kind;
            SetOutputValue("Value", agent.GetValue(Slot));
            SetOutputValue("Pressed", kind == BrainOutputKind.Button && agent.GetButton(Slot));
            string option = "";
            int choice = kind == BrainOutputKind.Choice ? agent.GetChoice(Slot, out option) : -1;
            SetOutputValue("Choice", choice);
            SetOutputValue("Option", option);
        }
    }

    /// <summary>
    /// "The right answer was ...": teaches an output slot this tick (an axis value, a button as Pressed, a choice's
    /// option index). E.g. wire the player's own input here so the bot learns by watching.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Teach Brain Output"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""\""The right answer was ...\"": teaches an output slot this tick (an axis value, a button as Pressed, a choice's option index). E.g. wire the player's own input here so the bot learns by watching."", ""inputs"": [{""name"": ""Value"", ""type"": ""float""}, {""name"": ""Pressed"", ""type"": ""bool""}, {""name"": ""Choice"", ""type"": ""int""}, {""name"": ""Brain Of"", ""type"": ""GameObject""}], ""use_for"": [""training an AI by example, e.g. wiring in the player's input""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Teach Brain Output", Path = "AI/Brain"), Tags("Object")]
    public class TeachBrainOutputNode : BrainNodeBase
    {
        [Editable, BrainSlot(true)] public string Slot = "Output";
        [Input("Value")] public float Value;
        [Input("Pressed")] public bool Pressed;
        [Input("Choice")] public int Choice;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var agent = Agent(instance);
            int index = agent.Definition.FindOutput(Slot);
            if (index < 0)
                throw new InvalidOperationException($"The brain has no output \"{Slot}\".");
            switch (agent.Definition.Outputs[index].Kind)
            {
                case BrainOutputKind.Axis: agent.Teach(Slot, GetInputValue("Value", Value)); break;
                case BrainOutputKind.Button: agent.Teach(Slot, GetInputValue("Pressed", Pressed) ? 1f : 0f); break;
                case BrainOutputKind.Choice: agent.Teach(Slot, GetInputValue("Choice", Choice)); break;
            }
        }
    }

    /// <summary>
    /// Praise (positive) or punish (negative) the brain. The reward credits its recent decisions, most of all the
    /// latest ones, fading by the definition's Credit Half-Life.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Reward Brain"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""Praise (positive) or punish (negative) the brain. The reward credits its recent decisions, most of all the latest ones, fading by the definition's Credit Half-Life."", ""inputs"": [{""name"": ""Reward"", ""type"": ""float""}, {""name"": ""Brain Of"", ""type"": ""GameObject""}], ""use_for"": [""rewarding good AI behavior (reaching goals, hitting targets)"", ""punishing bad behavior (taking damage, falling)""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Positive praises, negative punishes.""}")]
    [Serializable]
    [Node(Name = "Reward Brain", Path = "AI/Brain"), Tags("Object")]
    public class RewardBrainNode : BrainNodeBase
    {
        [Input("Reward")] public float Reward = 1f;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
            => Agent(instance).AddReward(GetInputValue("Reward", Reward));
    }

    /// <summary>Forgets the past: the brain's memory (its heads) back at rest, for this instance.</summary>
    [SchematicDoc(@"{""name"": ""Reset Brain Memory"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""Forgets the past: the brain's memory (its heads) back at rest, for this instance."", ""inputs"": [{""name"": ""Brain Of"", ""type"": ""GameObject""}], ""use_for"": [""a fresh start for an AI after respawning""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Reset Brain Memory", Path = "AI/Brain"), Tags("Object")]
    public class ResetBrainMemoryNode : BrainNodeBase
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var agent = Agent(instance);
            agent.Network?.Net.ResetState(agent.Row);
        }
    }

    /// <summary>Gives one brain's weights to another (same Brain Definition layout). Shared brains change for everyone.</summary>
    [SchematicDoc(@"{""name"": ""Copy Brain"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""Gives one brain's weights to another (same Brain Definition layout). Shared brains change for everyone."", ""inputs"": [{""name"": ""From"", ""type"": ""GameObject""}, {""name"": ""To"", ""type"": ""GameObject""}], ""use_for"": [""giving the best AI's skills to others""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Copy Brain", Path = "AI/Brain"), Tags("Object")]
    public class CopyBrainNode : SchematicActionNode
    {
        [Input("From")] public GameObject From;
        [Input("To")] public GameObject To;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var from = BrainBreeding.Require(GetInputValue<GameObject>("From", From), "From");
            var to = BrainBreeding.Require(GetInputValue<GameObject>("To", To), "To");
            BrainBreeding.Copy(from, to);
        }
    }

    /// <summary>
    /// Crossbreeds two brains into a third: Blend averages the weights, Swap Parts takes whole parts (the master, each
    /// head, the translator) from one parent or the other, Per Weight flips a coin for each. Amount is how much
    /// comes from B.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Mix Brains"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""Crossbreeds two brains into a third: Blend averages the weights, Swap Parts takes whole parts (the master, each head, the translator) from one parent or the other, Per Weight flips a coin for each. Amount is how much comes from B."", ""inputs"": [{""name"": ""A"", ""type"": ""GameObject""}, {""name"": ""B"", ""type"": ""GameObject""}, {""name"": ""Into"", ""type"": ""GameObject""}, {""name"": ""Amount"", ""type"": ""float""}], ""use_for"": [""breeding AI creatures"", ""evolution mechanics""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Mix Brains", Path = "AI/Brain"), Tags("Object")]
    public class MixBrainsNode : SchematicActionNode
    {
        [Input("A")] public GameObject A;
        [Input("B")] public GameObject B;
        [Input("Into")] public GameObject Into;
        [Input("Amount")] public float Amount = 0.5f;
        [Editable] public MnnBreeding.MixStyle Style = MnnBreeding.MixStyle.SwapParts;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var a = BrainBreeding.Require(GetInputValue<GameObject>("A", A), "A");
            var b = BrainBreeding.Require(GetInputValue<GameObject>("B", B), "B");
            var into = BrainBreeding.Require(GetInputValue<GameObject>("Into", Into) ?? instance, "Into");
            BrainBreeding.Mix(a, b, into, Style, GetInputValue("Amount", Amount));
        }
    }

    /// <summary>Adds a little random noise to a brain's weights (Strength is the noise's typical size).</summary>
    [SchematicDoc(@"{""name"": ""Mutate Brain"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Brain"", ""description"": ""Adds a little random noise to a brain's weights (Strength is the noise's typical size)."", ""inputs"": [{""name"": ""Strength"", ""type"": ""float""}, {""name"": ""Brain Of"", ""type"": ""GameObject""}], ""use_for"": [""adding variety between AI offspring""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Mutate Brain", Path = "AI/Brain"), Tags("Object")]
    public class MutateBrainNode : BrainNodeBase
    {
        [Input("Strength")] public float Strength = 0.02f;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
            => BrainBreeding.Mutate(Agent(instance), GetInputValue("Strength", Strength));
    }

    /// <summary>Copying and breeding brains between instances.</summary>
    public static class BrainBreeding
    {
        private static uint _seed = 12345;

        public static BrainAgent Require(GameObject gameObject, string role)
        {
            var agent = Brain.AgentOn(gameObject);
            if (agent?.Network == null)
                throw new InvalidOperationException($"{role}: {(gameObject != null ? gameObject.name : "nothing connected")} has no running Brain.");
            return agent;
        }

        private static void CheckLayout(BrainAgent a, BrainAgent b)
        {
            if (a.Network.Settings.LayoutSignature != b.Network.Settings.LayoutSignature)
                throw new InvalidOperationException("Only brains with the same layout (slots, network settings and seed) can share weights.");
        }

        public static void Copy(BrainAgent from, BrainAgent to)
        {
            CheckLayout(from, to);
            if (from.Network == to.Network) return;
            to.Network.Net.SetParameters(from.Network.Net.GetParameters());
        }

        public static void Mix(BrainAgent a, BrainAgent b, BrainAgent into, MnnBreeding.MixStyle style, float amount)
        {
            CheckLayout(a, b);
            CheckLayout(a, into);
            var child = MnnBreeding.Mix(a.Network.Settings, a.Network.Net.GetParameters(), b.Network.Net.GetParameters(), style, amount, _seed = MnnHash.Pcg(_seed));
            into.Network.Net.SetParameters(child);
        }

        public static void Mutate(BrainAgent agent, float strength)
        {
            var parameters = agent.Network.Net.GetParameters();
            MnnBreeding.Mutate(parameters, strength, _seed = MnnHash.Pcg(_seed));
            agent.Network.Net.SetParameters(parameters);
        }
    }

    #region Senses: general nodes for building what a brain perceives

    /// <summary>A fan of rays from an object (Box3D physics): each ray's hit distance, 0 (touching) to 1 (nothing within range).</summary>
    [SchematicDoc(@"{""name"": ""Ray Fan"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Senses"", ""description"": ""A fan of rays from an object (Box3D physics): each ray's hit distance, 0 (touching) to 1 (nothing within range)."", ""inputs"": [{""name"": ""From"", ""type"": ""GameObject""}, {""name"": ""Rays"", ""type"": ""int""}, {""name"": ""Spread"", ""type"": ""float""}, {""name"": ""Range"", ""type"": ""float""}, {""name"": ""Height"", ""type"": ""float""}, {""name"": ""Layers"", ""type"": ""LayerMask""}], ""outputs"": [{""name"": ""Distances"", ""type"": ""float[]""}, {""name"": ""Closest"", ""type"": ""float""}], ""use_for"": [""AI sight and obstacle sensing"", ""detecting walls around a character""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Ray Fan", Path = "AI/Senses"), Tags("Object")]
    public class RayFanNode : SchematicActionNode
    {
        [Input("From")] public GameObject From;
        [Input("Rays")] public int Rays = 7;
        [Input("Spread")] public float Spread = 120f;
        [Input("Range")] public float Range = 10f;
        [Input("Height")] public float Height = 0.5f;
        [Input("Layers")] public LayerMask Layers = ~0;

        [Output("Distances")] public float[] Distances;
        [Output("Closest")] public float Closest;

        private float[] _buffer;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var from = GetInputValue<GameObject>("From", From) ?? instance;
            int rays = Mathf.Clamp(GetInputValue("Rays", Rays), 1, 64);
            float spread = GetInputValue("Spread", Spread), range = Mathf.Max(0.01f, GetInputValue("Range", Range));
            LayerMask layers = GetInputValue("Layers", Layers);
            var transform = from.transform;
            Vector3 origin = transform.position + Vector3.up * GetInputValue("Height", Height);

            if (_buffer == null || _buffer.Length != rays) _buffer = new float[rays];
            float closest = 1f;
            for (int r = 0; r < rays; r++)
            {
                float angle = rays == 1 ? 0f : Mathf.Lerp(-spread * 0.5f, spread * 0.5f, r / (rays - 1f));
                Vector3 direction = Quaternion.AngleAxis(angle, transform.up) * transform.forward;
                float distance = SchematicPhysics.Raycast(origin, direction, out var hit, range, layers) ? hit.distance / range : 1f;
                _buffer[r] = distance;
                closest = Mathf.Min(closest, distance);
            }
            SetOutputValue("Distances", (float[])_buffer.Clone());
            SetOutputValue("Closest", closest);
        }
    }

    /// <summary>Where a target is from an object's point of view: its local direction (x right, z forward) and distance.</summary>
    [SchematicDoc(@"{""name"": ""Direction To"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Senses"", ""description"": ""Where a target is from an object's point of view: its local direction (x right, z forward) and distance."", ""inputs"": [{""name"": ""From"", ""type"": ""GameObject""}, {""name"": ""Target"", ""type"": ""Vector3""}], ""outputs"": [{""name"": ""Local Direction"", ""type"": ""Vector3""}, {""name"": ""Values"", ""type"": ""float[]""}, {""name"": ""Distance"", ""type"": ""float""}, {""name"": ""Angle"", ""type"": ""float""}], ""use_for"": [""aiming"", ""steering toward or away from targets"", ""AI senses""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Direction To", Path = "AI/Senses"), Tags("Object")]
    public class DirectionToNode : SchematicActionNode
    {
        [Input("From")] public GameObject From;
        [Input("Target")] public Vector3 Target;

        [Output("Local Direction")] public Vector3 LocalDirection;
        [Output("Values")] public float[] Values;
        [Output("Distance")] public float Distance;
        [Output("Angle")] public float Angle;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var from = (GetInputValue<GameObject>("From", From) ?? instance).transform;
            Vector3 offset = GetInputValue("Target", Target) - from.position;
            Vector3 local = from.InverseTransformDirection(offset);
            Vector3 direction = local.sqrMagnitude > 1e-8f ? local.normalized : Vector3.zero;
            SetOutputValue("Local Direction", direction);
            SetOutputValue("Values", new[] { direction.x, direction.y, direction.z });
            SetOutputValue("Distance", offset.magnitude);
            SetOutputValue("Angle", Vector3.SignedAngle(from.forward, Vector3.ProjectOnPlane(offset, from.up), from.up));
        }
    }

    /// <summary>The nearest object with a tag within range: it, its position, and how far.</summary>
    [SchematicDoc(@"{""name"": ""Nearest With Tag"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Senses"", ""description"": ""The nearest object with a tag within range: it, its position, and how far."", ""inputs"": [{""name"": ""From"", ""type"": ""GameObject""}, {""name"": ""Tag"", ""type"": ""string""}, {""name"": ""Range"", ""type"": ""float""}], ""outputs"": [{""name"": ""Found"", ""type"": ""bool""}, {""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Position"", ""type"": ""Vector3""}, {""name"": ""Distance"", ""type"": ""float""}], ""use_for"": [""finding the closest enemy, player or pickup""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Nearest With Tag", Path = "AI/Senses"), Tags("Object")]
    public class NearestWithTagNode : SchematicActionNode
    {
        [Input("From")] public GameObject From;
        [Input("Tag")] public string Tag = "Player";
        [Input("Range")] public float Range = 30f;

        [Output("Found")] public bool Found;
        [Output("Object")] public GameObject Object;
        [Output("Position")] public Vector3 Position;
        [Output("Distance")] public float Distance;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var from = (GetInputValue<GameObject>("From", From) ?? instance).transform.position;
            string tag = GetInputValue("Tag", Tag);
            float range = GetInputValue("Range", Range);
            GameObject best = null;
            float bestDistance = range;
            GameObject[] tagged;
            try { tagged = GameObject.FindGameObjectsWithTag(tag); }
            catch (UnityException) { tagged = Array.Empty<GameObject>(); }
            foreach (var candidate in tagged)
            {
                if (candidate == instance) continue;
                float distance = Vector3.Distance(from, candidate.transform.position);
                if (distance <= bestDistance) { bestDistance = distance; best = candidate; }
            }
            SetOutputValue("Found", best != null);
            SetOutputValue("Object", best);
            SetOutputValue("Position", best != null ? best.transform.position : from);
            SetOutputValue("Distance", best != null ? bestDistance : range);
        }
    }

    /// <summary>Several numbers as one list, for a multi-value brain input.</summary>
    [SchematicDoc(@"{""name"": ""Make Values"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Senses"", ""description"": ""Several numbers as one list, for a multi-value brain input."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}, {""name"": ""C"", ""type"": ""float""}, {""name"": ""D"", ""type"": ""float""}, {""name"": ""Count"", ""type"": ""int""}], ""outputs"": [{""name"": ""Values"", ""type"": ""float[]""}], ""use_for"": [""packing several numbers into one brain input""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Name = "Make Values", Path = "AI/Senses"), Tags("Default")]
    public class MakeValuesNode : SchematicActionNode
    {
        [Input("A")] public float A;
        [Input("B")] public float B;
        [Input("C")] public float C;
        [Input("D")] public float D;
        [Input("Count")] public int Count = 2;
        [Output("Values")] public float[] Values;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            int count = Mathf.Clamp(GetInputValue("Count", Count), 1, 4);
            var all = new[] { GetInputValue("A", A), GetInputValue("B", B), GetInputValue("C", C), GetInputValue("D", D) };
            var values = new float[count];
            Array.Copy(all, values, count);
            SetOutputValue("Values", values);
        }
    }

    #endregion
}
