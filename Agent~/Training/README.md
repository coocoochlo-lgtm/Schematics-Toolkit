# Training Sprocket's editor-token model

Sprocket can use a small model trained just for editing Schematic Graphs. It reads the editor and your request as
**editor tokens**, one token per node type and Behavior, namespaced by assembly so modules never clash. It writes
commands as tokens too. That's much shorter than JSON, and it can't misspell a node or invent one that doesn't exist.

## Steps
1. **Unity**: *Tools > Remedy > Schematics > Assistant > Training > Generate Training Data…*
   - Writes `data/vocab.tsv` (the tokens) and `data/dataset.jsonl` (about 20,000 example requests with their answers),
     built from the node types and Behaviors in your project. Every example is decoded back and checked first.
   - *Export Vocabulary* writes just the tokens.
2. **Train**: open `train_macro_model.ipynb` in [Google Colab](https://colab.research.google.com) (File > Upload notebook) or Kaggle,
   pick a GPU runtime, run everything, and upload the two files when asked. A free T4 is enough; it takes roughly
   30–90 minutes with rewording on.
3. **Use it**: put the downloaded `sprocket-macro.gguf` and `sprocket-macro.vocab.tsv` in Sprocket's `models`
   folder and restart the server (Preferences > Schematics Assistant > Stop, then Start). The server log says
   `[macro] Loaded the editor-token model`. Edit requests go to it first. If what it writes doesn't decode cleanly,
   the general model handles the request as before.

Retrain after adding many new node types or Behaviors. Ones the model hasn't seen still appear in its input
(as quoted names), but it can only *create* the ones in its vocabulary.

## The language (shared code)
`Schematics/Editor/Assistant/Macro/MacroLanguage.cs` defines it, and both Unity and the server compile that same
file. An example:

```
<state>
<node> <ref:1> <Remedy.Schematics/node/OnUpdateNode> <out> <flow>
<object> <ref:2> "Player"
<behavior> <ref:3> <Remedy.Movement/behavior/Mover> <var> "Speed" "Single" <signal> "Jump" <dir:in>
</state>
<request> make the player jump every frame
<macro> <add_signal> <new:1> <ref:3> "Jump" <mode:send>
 <connect> <ref:1> <flow> <new:1> <flow>
 <end>
```

- `<ref:n>`: things in the state (nodes, then objects and their Behaviors), numbered in order.
- `<new:n>`: nodes the answer creates.
- Names people chose (Variables, Signals, ports, values) stay as quoted text the model copies.
