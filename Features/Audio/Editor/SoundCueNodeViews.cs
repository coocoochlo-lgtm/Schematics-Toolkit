using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Audio
{
    /// <summary>
    /// The Generator node: sfxr's buttons (a preset for each kind of sound, Mutate, Randomize) and its settings as
    /// sliders, under a foldout. Every change can be undone, and a preview playing the cue replays with it.
    /// </summary>
    [CustomNodeView(typeof(SoundGeneratorNode))]
    public class SoundGeneratorNodeView : NodeView
    {
        private static readonly (string label, SfxrSettings.Preset preset)[] Presets =
        {
            ("Pickup", SfxrSettings.Preset.PickupCoin), ("Laser", SfxrSettings.Preset.LaserShoot),
            ("Explosion", SfxrSettings.Preset.Explosion), ("Power Up", SfxrSettings.Preset.PowerUp),
            ("Hit", SfxrSettings.Preset.HitHurt), ("Jump", SfxrSettings.Preset.Jump),
            ("Blip", SfxrSettings.Preset.BlipSelect), ("Random", SfxrSettings.Preset.Random),
        };

        private VisualElement _settings;
        private Foldout _foldout;

        private SoundGeneratorNode Generator => (SoundGeneratorNode)Target;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            style.minWidth = 240;

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 4, paddingRight = 4, paddingTop = 2 } };
            foreach (var (label, preset) in Presets)
            {
                var p = preset;
                buttons.Add(SmallButton(label, $"A new {label.ToLowerInvariant()} sound, picked at random.", () => Change($"Generator: {label}", () =>
                    Generator.Settings = SfxrSettings.FromPreset(p, NewSeed()))));
            }
            buttons.Add(SmallButton("Mutate", "Nudge every setting a little.", () => Change("Generator: Mutate", () =>
            {
                Generator.Settings ??= new SfxrSettings();
                Generator.Settings.Mutate(NewSeed());
            })));
            extensionContainer.Add(buttons);

            _foldout = new Foldout { text = "Settings", value = false };
            _settings = new VisualElement();
            _foldout.Add(_settings);
            extensionContainer.Add(_foldout);
            BuildSettings();
            RefreshExpandedState();
        }

        private static int NewSeed() => UnityEngine.Random.Range(1, int.MaxValue);

        private static Button SmallButton(string text, string tooltip, Action click)
        {
            var button = new Button(click) { text = text, tooltip = tooltip };
            button.style.marginLeft = button.style.marginRight = 1;
            button.style.marginTop = button.style.marginBottom = 1;
            button.style.paddingLeft = button.style.paddingRight = 4;
            return button;
        }

        // Every setting: the wave as a dropdown, ranged numbers as sliders, the seed as a number.
        private void BuildSettings()
        {
            _settings.Clear();
            var settings = Generator.Settings ??= new SfxrSettings();
            foreach (var field in typeof(SfxrSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var f = field;
                string label = ObjectNames.NicifyVariableName(f.Name);
                string tooltip = f.GetCustomAttribute<TooltipAttribute>()?.tooltip;
                VisualElement control;
                if (f.FieldType == typeof(SfxrSettings.WaveShape))
                {
                    var wave = new EnumField(label, (SfxrSettings.WaveShape)f.GetValue(settings));
                    wave.RegisterValueChangedCallback(evt => Change("Generator: " + label, () => f.SetValue(Generator.Settings, evt.newValue), rebuild: false));
                    control = wave;
                }
                else if (f.FieldType == typeof(float))
                {
                    var range = f.GetCustomAttribute<RangeAttribute>();
                    var slider = new Slider(label, range?.min ?? 0f, range?.max ?? 1f) { value = (float)f.GetValue(settings), showInputField = true };
                    slider.RegisterValueChangedCallback(evt => Change("Generator: " + label, () => f.SetValue(Generator.Settings, evt.newValue), rebuild: false));
                    control = slider;
                }
                else if (f.FieldType == typeof(int))
                {
                    var number = new IntegerField(label) { value = (int)f.GetValue(settings) };
                    number.RegisterValueChangedCallback(evt => Change("Generator: " + label, () => f.SetValue(Generator.Settings, evt.newValue), rebuild: false));
                    control = number;
                }
                else
                    continue;
                control.tooltip = tooltip;
                if (control is BaseField<float> b) b.labelElement.style.minWidth = 110;
                _settings.Add(control);
            }
        }

        private void Change(string undoName, Action change, bool rebuild = true)
        {
            var graph = Generator.Graph;
            if (graph != null)
                Undo.RegisterCompleteObjectUndo(graph, undoName);
            change();
            Generator.Settings?.Clamp();
            if (graph != null)
                EditorUtility.SetDirty(graph);
            OnPropertyChange();
            if (rebuild)
                BuildSettings();
        }
    }

    /// <summary>The Mixer node: + and − under its inputs to add one or remove the last (and its wire).</summary>
    [CustomNodeView(typeof(SoundMixerNode))]
    public class SoundMixerNodeView : NodeView
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            var node = (SoundMixerNode)Target;
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexStart, paddingLeft = 4, paddingTop = 2, paddingBottom = 2 } };
            var remove = new Button(() => Change(add: false)) { text = "−", tooltip = "Remove the last input (and its wire)" };
            var add = new Button(() => Change(add: true)) { text = "+", tooltip = "Add an input" };
            foreach (var button in new[] { remove, add })
            {
                button.style.width = 20;
                button.style.height = 16;
                button.style.paddingLeft = button.style.paddingRight = 0;
                row.Add(button);
            }
            remove.SetEnabled(node.InputCount > SoundMixerNode.MinInputs);
            add.SetEnabled(node.InputCount < SoundMixerNode.MaxInputs);
            inputContainer.Add(row);
        }

        private void Change(bool add)
        {
            var node = (SoundMixerNode)Target;
            var graph = node.Graph;
            if (graph != null)
                Undo.RegisterCompleteObjectUndo(graph, add ? "Add Mixer Input" : "Remove Mixer Input");
            if (add) node.AddInput();
            else node.RemoveInput();
            if (graph != null)
                EditorUtility.SetDirty(graph);
            var canvas = Canvas;
            canvas?.schedule.Execute(canvas.Reload);
        }
    }
}
