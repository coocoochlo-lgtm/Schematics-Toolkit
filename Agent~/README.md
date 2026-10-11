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

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. In Unity, run **Tools > Remedy > Schematics > Assistant > Set Up**. You can also run `setup.ps1` (Windows) or
   `setup.sh` (macOS/Linux) in this folder yourself. Setup downloads the models into `models/` and builds the server
   into `bin/`.
   - For an NVIDIA GPU, pick **Cuda12** under **Preferences > Schematics Assistant > Model Backend** first.
   - Or run `setup.ps1 -Backend Cuda12` (`Vulkan` for other GPUs).
3. Open a Schematic Graph and run **Tools > Remedy > Schematics > Assistant > Open Assistant**. Unity starts the
   server in the background, on `127.0.0.1:5317`, and stops it when the editor closes.

## Using it

- **Click** Sprocket to type a request.
- **Hold Q** over the graph window to talk. You can rebind it in **Edit > Shortcuts > Schematics/Assistant**.
- **Drag** Sprocket anywhere while it's idle.
- **Esc**, or a click while it's working, stops it.
- **Right-click** for preferences:
  - voice and speed;
  - microphone;
  - the delay between steps.

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
