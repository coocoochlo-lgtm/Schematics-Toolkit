namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/instruments.json")]
public class Instruments
{
    public class Root
    {
        public object? graphstate { get; set; }
        public Instrument[]? instruments { get; set; }
        public string? message { get; set; }
        public string? name { get; set; }
        public object? orderlist { get; set; }
        public Pattern[]? patterns { get; set; }
        public object? pluginstate { get; set; }
        public Sample[]? samples { get; set; }
        public int version { get; set; }
    }

    public class Instrument
    {
        public int default_filter_cutoff { get; set; }
        public bool default_filter_cutoff_enabled { get; set; }
        public int default_filter_mode { get; set; }
        public int default_filter_resonance { get; set; }
        public bool default_filter_resonance_enabled { get; set; }
        public int default_pan { get; set; }
        public int duplicate_check_type { get; set; }
        public int duplicate_note_action { get; set; }
        public int fadeout { get; set; }
        public int global_volume { get; set; }
        public int graph_insert { get; set; }
        public string? legacy_filename { get; set; }
        public int midi_bank { get; set; }
        public int midi_channel { get; set; }
        public int midi_drum_set { get; set; }
        public int midi_program { get; set; }
        public string? name { get; set; }
        public int new_note_action { get; set; }
        public object? note_map { get; set; }
        public Envelope? panning_envelope { get; set; }
        public Envelope? pitch_envelope { get; set; }
        public int pitch_pan_center { get; set; }
        public int pitch_pan_separation { get; set; }
        public int pitch_to_tempo_lock { get; set; }
        public int random_cutoff_weight { get; set; }
        public int random_pan_weight { get; set; }
        public int random_resonance_weight { get; set; }
        public int random_volume_weight { get; set; }
        public object? sample_map { get; set; }
        public object? tuning { get; set; }
        public Envelope? volume_envelope { get; set; }
        public int volume_ramp_down { get; set; }
        public int volume_ramp_up { get; set; }
    }

    public class Envelope
    {
        public int loop_end { get; set; }
        public int loop_start { get; set; }
        public EnvelopeNode[]? nodes { get; set; }
        public int release_node { get; set; }
        public int sustain_end { get; set; }
        public int sustain_start { get; set; }
    }

    public class EnvelopeNode
    {
        public int tick { get; set; }
        public int value { get; set; }
    }

    public class Pattern
    {
        public PatternRow[]? data { get; set; }
        public string? name { get; set; }
        public int rows { get; set; }
        public int rows_per_beat { get; set; }
        public int rows_per_measure { get; set; }
    }

    public class PatternRow
    {
        public int channel { get; set; }
        public int fxcmd { get; set; }
        public int fxparam { get; set; }
        public int instr { get; set; }
        public int note { get; set; }
        public int row { get; set; }
        public int volcmd { get; set; }
        public int volval { get; set; }
    }

    public class Sample
    {
        public int c5_samplerate { get; set; }
        public int global_volume { get; set; }
        public string? legacy_filename { get; set; }
        public int length { get; set; }
        public int loop_end { get; set; }
        public int loop_start { get; set; }
        public string? name { get; set; }
        public int pan { get; set; }
        public int sustain_end { get; set; }
        public int sustain_start { get; set; }
        public int vibrato_depth { get; set; }
        public int vibrato_rate { get; set; }
        public int vibrato_sweep { get; set; }
        public int vibrato_type { get; set; }
        public int volume { get; set; }
    }
}