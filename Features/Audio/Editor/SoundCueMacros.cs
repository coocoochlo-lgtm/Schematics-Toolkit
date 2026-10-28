using Remedy.Schematics.Macros;
using UnityEditor;

namespace Remedy.Audio
{
    /// <summary>
    /// Sound Cue macros (right-click in a cue: Macros): ready-made sounds built from nodes, with their settings as
    /// Parameters (set from gameplay with Set Graph Parameters) and plain numbers on the nodes to tune.
    /// </summary>
    [InitializeOnLoad]
    internal static class SoundCueMacros
    {
        private const float Column = 260f, Row = 150f;

        static SoundCueMacros()
        {
            SchematicMacros.Register(new BuiltInMacro
            {
                Path = "Vehicles/Car Engine (realistic)",
                Description = "A four-cylinder engine: RPM sets its firing rate, Throttle how hard it's working.",
                GraphType = typeof(SoundCueGraph),
                Build = CarEngine,
            });
            SchematicMacros.Register(new BuiltInMacro
            {
                Path = "Footsteps/Footstep on Any Surface",
                Description = "A step synthesised from whatever's underfoot (its Surface Material): Speed sets how hard.",
                GraphType = typeof(SoundCueGraph),
                Build = Footstep,
            });
            SchematicMacros.Register(new BuiltInMacro
            {
                Path = "Retro/Pickup Arpeggio",
                Description = "A coin-style blip played up a chord.",
                GraphType = typeof(SoundCueGraph),
                Build = PickupArpeggio,
            });
            SchematicMacros.Register(new BuiltInMacro
            {
                Path = "Listener Chains/Underwater",
                Description = "For a Listener Chain: Underwater (0 to 1) muffles and washes the whole mix.",
                GraphType = typeof(SoundCueGraph),
                Build = Underwater,
            });
            SchematicMacros.Register(new BuiltInMacro
            {
                Path = "Listener Chains/Ringing Ears",
                Description = "For a Listener Chain: Ringing (0 to 1) dulls the mix and rings a high tone over it.",
                GraphType = typeof(SoundCueGraph),
                Build = RingingEars,
            });
        }

        // Wires the macro's last node into the cue's Output when nothing's plugged in there yet (and, for sounds that
        // run until they're stopped, makes the Output Endless).
        private static void ToOutput(MacroBuilder builder, SoundNode last, string port, bool endless)
        {
            var output = builder.Existing<SoundCueOutput>();
            if (output == null)
                return;
            if (output.GetPort(SoundCueOutput.AudioPort)?.ConnectionCount == 0)
                builder.Link(last, port, output, SoundCueOutput.AudioPort);
            if (endless)
                builder.AfterInsert += () => output.Length = SoundCueOutput.LengthMode.Endless;
        }

        private static SoundParameterNode Parameter(MacroBuilder b, float x, float y, string name, float value)
            => b.Add<SoundParameterNode>(x, y, n => { n.ParameterName = name; n.Default = value; n.Name = name; });

        private static SoundMapRangeNode Map(MacroBuilder b, float x, float y, string name, SoundNode from, float inMin, float inMax, float outMin, float outMax)
        {
            var map = b.Add<SoundMapRangeNode>(x, y, n => { n.InMin = inMin; n.InMax = inMax; n.OutMin = outMin; n.OutMax = outMax; n.CustomName = name; });
            b.Link(from, from is SoundParameterNode ? "Value" : "Result", map, "Value");
            return map;
        }

        // Firing pulses (a saw at RPM x 4 / 120, from a Varispeed so RPM changes glide) through a resonant low pass
        // (the exhaust's body, opening up with RPM) and overdrive (harder with throttle); a half-rate rumble (the
        // crank's lumpiness); band-passed intake hiss (louder with throttle); all darker and quieter off throttle.
        private static void CarEngine(MacroBuilder b)
        {
            var rpm = Parameter(b, 0, 0, "RPM", 900f);
            var throttle = Parameter(b, 0, Row * 2, "Throttle", 0.2f);

            var firing = b.Add<SoundMultiplyNode>(Column, 0, n => { n.B = 4f / 120f; n.CustomName = "Firing Rate (x cylinders / 120)"; });
            b.Link(rpm, "Value", firing, "A");
            var bodyCutoff = Map(b, Column, Row, "Exhaust Cutoff", rpm, 800f, 7000f, 250f, 1800f);
            var intakeCutoff = Map(b, Column, Row * 2, "Intake Cutoff", rpm, 800f, 7000f, 900f, 4500f);
            var drive = Map(b, Column, Row * 3, "Overdrive", throttle, 0f, 1f, 1.5f, 7f);
            var intakeLevel = Map(b, Column, Row * 4, "Intake Level", throttle, 0f, 1f, 0.04f, 0.3f);
            var brightness = Map(b, Column, Row * 5, "Brightness", throttle, 0f, 1f, 1800f, 9000f);
            var level = Map(b, Column, Row * 6, "Level", throttle, 0f, 1f, 0.3f, 0.5f);

            var crank = b.Add<SoundVarispeedNode>(Column * 2, 0, n => n.CustomName = "Crank");
            b.Link(firing, "Result", crank, "Speed");
            var halfTime = b.Add<SoundMultiplyNode>(Column * 2, Row, n => { n.B = 0.5f; n.CustomName = "Half Rate"; });
            b.Link(crank, "Time", halfTime, "A");

            var pulses = b.Add<SoundOscillatorNode>(Column * 3, 0, n => { n.Wave = SoundOscillatorNode.Shape.Saw; n.Frequency = 1f; n.CustomName = "Firing Pulses"; });
            b.Link(crank, "Time", pulses, "Time");
            var rumble = b.Add<SoundOscillatorNode>(Column * 3, Row, n => { n.Wave = SoundOscillatorNode.Shape.Sine; n.Frequency = 1f; n.Amplitude = 0.3f; n.CustomName = "Crank Rumble"; });
            b.Link(halfTime, "Result", rumble, "Time");
            var hiss = b.Add<SoundOscillatorNode>(Column * 3, Row * 2, n => { n.Wave = SoundOscillatorNode.Shape.Noise; n.CustomName = "Intake Hiss"; });

            var exhaust = b.Add<SoundFilterNode>(Column * 4, 0, n => { n.Type = SoundFilterNode.Kind.LowPass; n.Resonance = 2.5f; n.CustomName = "Exhaust"; });
            b.Link(pulses, "Value", exhaust, "Audio");
            b.Link(bodyCutoff, "Result", exhaust, "Cutoff");
            var intake = b.Add<SoundFilterNode>(Column * 4, Row * 2, n => { n.Type = SoundFilterNode.Kind.BandPass; n.Resonance = 1.2f; n.CustomName = "Intake"; });
            b.Link(hiss, "Value", intake, "Audio");
            b.Link(intakeCutoff, "Result", intake, "Cutoff");

            var grit = b.Add<SoundDistortionNode>(Column * 5, 0, n => { n.Mix = 1f; n.CustomName = "Combustion Grit"; });
            b.Link(exhaust, "Out", grit, "Audio");
            b.Link(drive, "Result", grit, "Drive");
            var intakeVolume = b.Add<SoundVolumeNode>(Column * 5, Row * 2, n => n.CustomName = "Intake Volume");
            b.Link(intake, "Out", intakeVolume, "Audio");
            b.Link(intakeLevel, "Result", intakeVolume, "Volume");

            var mixer = b.Add<SoundMixerNode>(Column * 6, Row, n => { for (int i = 0; i < 3; i++) n.AddInput(); n.CustomName = "Engine Mix"; });
            b.Link(grit, "Out", mixer, SoundMixerNode.InputName(0));
            b.Link(rumble, "Value", mixer, SoundMixerNode.InputName(1));
            b.Link(intakeVolume, "Out", mixer, SoundMixerNode.InputName(2));

            var tone = b.Add<SoundFilterNode>(Column * 7, Row, n => { n.Type = SoundFilterNode.Kind.LowPass; n.Resonance = 0.707f; n.CustomName = "Off-Throttle Darkening"; });
            b.Link(mixer, "Out", tone, "Audio");
            b.Link(brightness, "Result", tone, "Cutoff");
            var volume = b.Add<SoundVolumeNode>(Column * 8, Row, n => n.CustomName = "Engine");
            b.Link(tone, "Out", volume, "Audio");
            b.Link(level, "Result", volume, "Volume");
            ToOutput(b, volume, "Out", endless: true);
        }

        // What's underfoot (Surface, looking down) shapes a synthesised Impact; Speed (0 to 6 m/s) how hard.
        private static void Footstep(MacroBuilder b)
        {
            var speed = Parameter(b, 0, 0, "Speed", 2f);
            var strength = Map(b, Column, 0, "Step Strength", speed, 0f, 6f, 0.25f, 1f);
            var surface = b.Add<SoundSurfaceNode>(Column, Row, n => n.CustomName = "Underfoot");
            var impact = b.Add<SoundImpactNode>(Column * 2, 0, n => { n.Size = 0.35f; n.Ring = 0.12f; n.CustomName = "Step"; });
            b.Link(strength, "Result", impact, "Strength");
            b.Link(surface, "Hardness", impact, "Hardness");
            b.Link(surface, "Absorption", impact, "Absorption");
            b.Link(surface, "Wetness", impact, "Wetness");
            ToOutput(b, impact, "Out", endless: false);
        }

        // An Arpeggiator's Note Time replays a coin blip at each note of a major chord.
        private static void PickupArpeggio(MacroBuilder b)
        {
            var arp = b.Add<SoundArpeggiatorNode>(0, 0, n => { n.Notes = "0 4 7 12"; n.Rate = 16f; n.Pattern = SoundArpeggiatorNode.ArpPattern.Up; n.CustomName = "Chord Steps"; });
            var blip = b.Add<SoundGeneratorNode>(Column, 0, n => { n.Settings = SfxrSettings.FromPreset(SfxrSettings.Preset.PickupCoin, 7); n.CustomName = "Blip"; });
            b.Link(arp, "Note Time", blip, "Time");
            var gate = b.Add<SoundVolumeNode>(Column * 2, 0, n => n.CustomName = "Gate");
            b.Link(blip, "Audio", gate, "Audio");
            b.Link(arp, "Gate", gate, "Volume");
            var fade = b.Add<SoundVolumeNode>(Column * 3, 0, n => { n.Volume = 0.7f; n.CustomName = "Level"; });
            b.Link(gate, "Out", fade, "Audio");
            ToOutput(b, fade, "Out", endless: false);
        }

        // The mix (Listener Input) low passed and washed with reverb, faded in by Underwater.
        private static void Underwater(MacroBuilder b)
        {
            var mix = b.Add<SoundListenerInputNode>(0, 0, n => n.CustomName = "Everything Heard");
            var amount = Parameter(b, 0, Row * 2, "Underwater", 0f);
            var muffle = b.Add<SoundFilterNode>(Column, Row, n => { n.Type = SoundFilterNode.Kind.LowPass; n.Cutoff = 450f; n.Resonance = 1.1f; n.CustomName = "Muffle"; });
            b.Link(mix, "Out", muffle, "Audio");
            var wash = b.Add<SoundReverbNode>(Column * 2, Row, n => { n.RoomSize = 0.8f; n.Damping = 0.8f; n.Mix = 0.45f; n.CustomName = "Wash"; });
            b.Link(muffle, "Out", wash, "Audio");
            var blend = b.Add<SoundCrossfadeNode>(Column * 3, 0, n => n.CustomName = "Dry to Underwater");
            b.Link(mix, "Out", blend, "A");
            b.Link(wash, "Out", blend, "B");
            b.Link(amount, "Value", blend, "Blend");
            ToOutput(b, blend, "Out", endless: true);
        }

        // The mix dulled by Ringing, with a high tone over it as loud as Ringing.
        private static void RingingEars(MacroBuilder b)
        {
            var mix = b.Add<SoundListenerInputNode>(0, 0, n => n.CustomName = "Everything Heard");
            var ringing = Parameter(b, 0, Row * 2, "Ringing", 0f);
            var cutoff = Map(b, Column, Row, "Dulling", ringing, 0f, 1f, 20000f, 700f);
            var toneLevel = Map(b, Column, Row * 3, "Tone Level", ringing, 0f, 1f, 0f, 0.08f);
            var dull = b.Add<SoundFilterNode>(Column * 2, 0, n => { n.Type = SoundFilterNode.Kind.LowPass; n.CustomName = "Dull"; });
            b.Link(mix, "Out", dull, "Audio");
            b.Link(cutoff, "Result", dull, "Cutoff");
            var tone = b.Add<SoundOscillatorNode>(Column * 2, Row * 2, n => { n.Wave = SoundOscillatorNode.Shape.Sine; n.Frequency = 3800f; n.CustomName = "Tinnitus"; });
            b.Link(toneLevel, "Result", tone, "Amplitude");
            var mixer = b.Add<SoundMixerNode>(Column * 3, Row, n => { n.AddInput(); n.AddInput(); n.CustomName = "Ears"; });
            b.Link(dull, "Out", mixer, SoundMixerNode.InputName(0));
            b.Link(tone, "Value", mixer, SoundMixerNode.InputName(1));
            ToOutput(b, mixer, "Out", endless: true);
        }
    }
}
