# Sprocket, the Schematics assistant

A little robot that lives in Schematic Graph windows. Ask it for something, by typing or by holding **Q**. It tells you
what it will do, out loud, and then flies around the graph doing it. It can:

- place nodes;
- drop in Signal, Variable and Behavior nodes from the IO Dock;
- wire up flow and value ports;
- set values;
- add Behaviors to the prefab;
- create objects in the scene.

Everything it does is one Undo step.

It runs fully on your computer. Nothing is sent anywhere.

| Part | What | Size |
| --- | --- | --- |
| Language model | Qwen2.5-1.5B-Instruct (Q4_K_M GGUF) via LLamaSharp / llama.cpp | ~1.1 GB |
| Speech to text | Whisper base.en via Whisper.net | ~150 MB |
| Voice | Kokoro (11 voices) via sherpa-onnx | ~330 MB |

## Setting up

Nothing to do. The first time Sprocket appears in a Schematic Graph window, it installs itself in the background and
shows its progress in its speech bubble. It:

1. uses your .NET 10 SDK, or fetches a private copy into `.dotnet/` if you don't have one;
2. downloads the models into `models/` (about 1.6 GB);
3. builds the server into `bin/`.

Then Unity starts the server on `127.0.0.1:5317` and stops it when the editor closes. A script reload doesn't
interrupt the install; Unity picks the progress back up.

- **Another drive:** in **Preferences > Schematics Assistant**, **Install Folder > Choose…** puts everything big there
  instead of here: .NET, the models, the server, the NuGet packages the build downloads (LLamaSharp's native libraries
  are large) and temporary files. It installs into a `SchematicsAssistant` folder inside the one you pick. An older
  install left here can be deleted from the same page. By hand: `setup.ps1 -InstallDir D:\Sprocket` or
  `setup.sh Cpu /path/to/folder`.
- **GPU speed-up:** in **Preferences > Schematics Assistant**, set **Model Backend** to **Cuda12** (NVIDIA) or
  **Vulkan**, then press **Reinstall**.
- **Turning it off:** the same page has **Install Automatically** and **Show in Graph Windows**.
- **By hand:** `setup.ps1` (Windows) or `setup.sh` (macOS/Linux) in this folder do the same thing. Progress goes to
  `install-status.txt` and the output to `setup.log`.

## Using it

- **Where it lives.** Sprocket appears with the first graph window. It may be in the Scene view (including Prefab
  Mode), the Game view, Schematic Graphs, the Inspector, the Hierarchy and the Project window, the places it can do
  something about.
  - **Drag** it around and over any of those windows. It moves into the one under the pointer straight away.
  - **Dropping it** anywhere else, or losing its window (closed, covered by another tab, too small), makes it
    spin-blip back to the bottom right of a Schematic Graph, or of the Scene view if no graph is showing.
  - **Tools > Remedy > Schematics > Assistant > Bring Sprocket Here** calls it over.
  - It hops into the graph by itself when it has graph work to do, then goes back to where it was.
- **"What is this?"** While you drag it, it turns see-through, and the tag by the pointer names what it's over.
  Drop it on something and it moves beside it, takes a look, and explains it out loud. It understands:
  - graph nodes. Signal and Variable nodes say so, then describe the Signal or Variable itself;
  - IO Dock Behaviors, Variables, Signals and objects;
  - objects in the Scene view and rows in the Hierarchy, described with their Behaviors and components;
  - assets in the Project window;
  - whatever the Inspector shows.

  The words come from each piece's `[SchematicDoc]`, from built-in descriptions of Unity's common components and
  assets, or from a hand-written **Description** in `Schematics/Editor/Assistant/Resources/SprocketDocs.asset`,
  which wins.
- **Lost Sprocket.** It respawns at the bottom right of the last window it was in. If that was the Hierarchy, it goes
  to the Scene view instead (or the Game view, if that's maximized).
- **Talking to it.**
  - **Click** Sprocket to type a request.
  - **Hold Q** to talk. That's a rebindable shortcut in graph windows (**Edit > Shortcuts > Schematics/Assistant**).
    In other windows it's a plain Q key while Sprocket is there, except in the Scene and Game views, where Q is a
    tool key.
  - **Esc**, or a click while it's working, stops it.
- **Settings.** **Right-click** for preferences: its size (whole multiples keep the pixel art crisp), voice and
  speed, microphone, and the delay between steps.
- **Its look.** The animations are the sprite frames in `Schematics/Editor/Styling/Resources/Textures/sprocket/`
  (`idle`, `thinking`, `talking`, `confused`, and `whatisthis` for inspecting), named `sprocket_<animation><number>` and played in number order.
  Sprocket is one sprite moved with a transform, so it never triggers a layout pass. It animates only while the
  editor has focus.

It only knows what's open:

- the graph;
- the prefab's objects with their Behaviors' Variables and Signals;
- the node types;
- the active scene.

It has no chat history, so each request stands alone.

## How it works

The server is `SchematicsAgent/`, an ASP.NET minimal API. This folder ends in `~`, so Unity ignores it.

- `POST /ask` takes `{ utterance, context }`. The context is the editor snapshot from `AssistantContext.cs`.
  [RE:Dox](../Dependencies/REDox-1.0.1) parses it, and the server condenses it to compact text. Then it runs the model
  twice, with GBNF grammars so the output is always valid JSON:
  1. A **reader** pass decides what to say and writes a short plan.
  2. An **actor** pass turns the plan into commands such as `add_node`, `add_signal_node`, `connect`, `set_value`.
  The reply is `{ say, commands[] }`.

  `AssistantCommands.cs` carries out the commands in the editor.
- `POST /transcribe` takes 16 kHz WAV and returns `{ text }`.
- `POST /speak` takes `{ text, voice, speed }` and returns WAV.
- `GET /voices` and `GET /health` report the available voices and the server's state.

RE:Dox needs C# 14, so it compiles here and not in Unity. Its asmdef in Unity is switched off.

### The API reference

Every node, Behavior, Variable and Signal carries a `[SchematicDoc(@"{json}")]`: its name, kind, description, and
what a small model needs to compose gameplay with it:
- `flow` and ports for nodes;
- `menu`, Variables and Signals for Behaviors;
- `type` and `default` for Variables;
- `direction` and `params` for Signals;
- `use_for` gameplay hints and wiring `notes`.

After every recompile, Unity sends the whole list to the server (`POST /reference/sync`). The server adds anything
new or changed to `reference/api.json`. If it isn't running, the list waits in `reference/incoming.json` and is
taken in on start.

Each request pulls in the most relevant entries for the reader and actor prompts, scored by shared words, with name
matches counting extra. `GET /reference/search?q=...` shows what a request would see. Document your own nodes and
Behaviors the same way, and the assistant can use them after the next recompile.

### Server options

Pass these as `dotnet bin/SchematicsAgent.dll --name value`:

- `--port`
- `--models`
- `--llm` (another GGUF, e.g. a Phi model)
- `--context`
- `--gpu-layers`
- `--threads`
- `--whisper`
- `--kokoro`
