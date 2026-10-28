# The Remedy Toolkit
*Build your game, not your infrastructure*

*A modular gameplay architecture framework for Unity that automates core infrastructure with minimal runtime overhead and can be integrated into any Unity project.*


[![Unity Version](https://img.shields.io/badge/Unity-2022%2B-blue.svg)](https://unity3d.com/get-unity/download)
[![Unity Version](https://img.shields.io/badge/Unity-6000%2B-blue.svg)](https://unity3d.com/get-unity/download)

## 🎯 What is The Remedy?

* Production-ready gameplay modules with reusable system implementations and extensible architecture    
  → Pooling  
  → Input  
  → Networking  
  → Character controllers  
  → Weapons
  
* Editor-first workflow with instant feedback   
  → See Tooling Overview

* Automated dependency injection with zero manual wiring  
  → See Automation Breakdown
  
* Native-performance asset-driven gameplay graphs    
  → See Graph Runtime & Performance
  
* Zero-allocation signal/event architecture   
  → See Signal Architecture & Benchmarks

* Reflection-free hot paths    
  → See Runtime Architecture
  
## Architecture Philosophy  
The Remedy centralizes common gameplay infrastructure patterns into a unified architecture layer while preserving project flexibility and modular adoption.

### Why The Remedy?
• Reduces boilerplate infrastructure setup  
• Avoids heavy global frameworks  
• Focuses on performance-oriented gameplay systems  

## Adoption Model  
The Remedy can be adopted incrementally:

• Use signals independently  
• Integrate graphs without replacing your systems  
• Enable infrastructure modules as needed  

No forced global architecture.

---

## 🎮 Powered by Remedy Toolkit

Here are some projects and demos built using The Remedy Toolkit:

| Game / Demo | Clip |
|------------|------|
| *Tails Adventure Armada Fangame* | ![Clip](https://media3.giphy.com/media/v1.Y2lkPTc5MGI3NjExY2hucWU5eGZ6dGF0aWgyMWlvcGtkMWd4ZXN6cDBsNW1kdHpuOGp6eCZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/nq8mTu1N24CCXH2Z5z/giphy.gif) |

> All clips are captured in real-time to showcase **performance, node-based workflows, and gameplay features** powered by Remedy Toolkit.

---

## 🚀 Core Features

### 🎨 **Schematics Editor**
![Clip](https://media4.giphy.com/media/v1.Y2lkPTc5MGI3NjExcjFuOXNxZHQ3dGUybHFxOTlqbHA2aTU1eXAzcjNzeWc3eHg4ZHcydiZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/GJL1EekvjLFr5P7mgl/giphy.gif)
- **Visual node-based development** Unreal Blueprint-inspired interface
- **IODock system** for drag-and-drop component wiring backed by automated ScriptableEvent creation and management
- **Prefab-driven architecture** with global system scope for systems like Input and Audio
- **Auto-generated ScriptableObjects** - Zero manual asset management, meaning you simply add a component and it's Input and Output and Data are all neatly generated behind the scenes

### ⚡ **Performance-First Engine**
![Clip](https://github.com/Cairo-Creative-Studios/The-Remedy-Toolkit/blob/main/media/character%20controller%20performance.gif)
- **Union-based value passing** - CPU cache optimized data structures with 0 GC Alloc for almost all Unity relevant Types
- **Zero reflection at runtime** - Pure ScriptableObject architecture
- **Automatic object pooling** for Schematic-based objects
- **Sub-0.1ms frame time** for complex physics based character controllers
- **Multiplatform** runs blazing fast on all platforms

### 🔧 **Gameplay Features**
![Clip](https://media2.giphy.com/media/v1.Y2lkPTc5MGI3NjExOHloaXFkb3duaWM0azA3MzZvY2dqZTl2NGRxNXBra3hrbnFxbzhjMSZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/SAP1lRPy721vcJdOVD/giphy.gif)
- **Character Controllers**: Hover, wall-slide, ledge-grab systems
- **Weapon System**: Projectile and melee weapons for any object type
- **Advanced Camera**: Flexible Cameras including a AAA-quality third-person camera (Mario Odyssey-style) and over the shoulder shooter camera 
- **Custom UI System**: Mesh-based UI with better performance than built in Unity UI frameworks
- **Audio Management**: Full control over the audio of the game from music to sound, sound cues are pooled elegantly 
- **Inventory Management**: Manage inventories, item collection, and more, use in conjunction with previous features for weapon pickup or displaying in the UI
- **Damageables**: Health and Damage management for characters and objects 

### 🏗️ **Enterprise Architecture**
- **Complete decoupling** - Remove any feature without errors
- **SOLID compliance** enforced by design
- **Event-driven communication** via automated ScriptableEvents  
- **Modular by default** - Pay only for what you use

---

## 📊 Performance Comparison

| System | Frame Time | Performance Factor |
|--------|------------|-------------------|
| Unity Visual Scripting | ~1.5ms | 1x (baseline) |
| **Remedy Toolkit** | **~0.1ms** | **15x faster** |

*Tested with full-featured rigidbody character controller including sphere-cast ground and wall detection*

---

### Your First Schematic in 60 Seconds
1. **Create a Prefab** The system will ask if you want to create a Schematic, click Yes
2. **Open Schematics Window** Opens automatically on first creation, then you can click the $ button where the prefab is displayed in the Object Hierarchy
3. **Setup your Prefab** Set up prefab components faster than the Unity Inspector using the Schematic IODock 
4. **Connect nodes** Drag-and-drop Events from the IODock to the Schematic Graph and wire them up to make your game object come to life
5. **Hit Play** - Watch it come to life

### Implement Schematics in Your Own Scripts:
```csharp
public class PlayerMovement : MonoBehaviour
{
    // Your component just needs to use Scriptable Events and it the Schematic Editor will pick find it and set it up for you...
    public ScriptableEvent<Vector3>.Input OnMoveInput;
    public ScriptableEvent<bool>.Output OnGrounded;

    // The Schematic Editor will also generate data for properties if you use the SchematicProperties Attribute...
    [SchematicProperties]
    public MyDataSO Properties;

    // Enable User Created Event Setups using IdentityLists... IdentityLists can use Identifier Fields to tell the items apart, which are fields that are customizable by the user using the given Identifier Type
    [IdentityListRenderer(identifierType: EventListIdentifierType.Name, identifierField: "Name", depth: 0, foldoutTitle: "Custom Events", itemName: "Event")]
    public List<CustomizableEvent> CustomEvents = new(); // You can also use arrays: CustomizableEvent[]

    private bool _isOnGround = false;

    private void OnEnable()
    {
      OnMoveInput.Subscribe(this, //function or lambda);
    }

    private void OnDisable()
    {
      OnMoveInput.Unsubscribe(this);
    }

    //...

    private void Update()
    {
      // Ground Check
      OnGrounded?.Invoke(_isOnGround);
    }

    [Serializable]
    public class CustomizableEvent 
    {
        public string Name;

        // All fields/properties of items in the list are also managed by the IODock
        [IdentityListRenderer(identifierType: EventListIdentifierType.Dropdown, identifierField: "Type", depth: 1, foldoutTitle: "Nested Events", itemName: "Event")]
        public NestedEvent[] NestedEvents = new NestedEvents[0];
        public ScriptableEvent.Output CustomOutput;
        [SchematicProperties]
        public CustomProperties Properties;

        [Serializable]
        public class NestedEvent
        {
          //...
        }
    }
}
```

### Schematic Behaviors
Schematic Behaviors work like Systems rather than MonoBehaviours: there is one of each, and its Runtime calls its lifecycle methods for every object it's attached to. Attach them with the **Add Schematic Behavior** button under the GameObject header of a Schematic Object or any of its children.

```csharp
[SchematicBehaviorPath("Movement/Spinner")]
public class Spinner : SchematicBehavior
{
    // Per-instance Variables. Defaults can be changed per object in the Inspector.
    private readonly VariableHandle<float> _degreesPerSecond = 90f;
    private readonly VariableHandle<float> _spun = 0f;

    protected override void OnStart() { }   // every spawn, Variables already reset
    protected override void OnEnable() { }

    protected override void OnUpdate()
    {
        float step = _degreesPerSecond * Time.deltaTime;
        _spun.Value += step;
        Self.Transform.Rotate(0f, step, 0f);
    }
}
```

Lifecycle methods run once per object, with that object's instance as `Self`; Variables and Signals act on `Self`. To act on another object's instance of the same Behavior, pick it for a block. `Self` switches back afterwards:

```csharp
using (Pick(otherUid))          // also Pick(gameObject) or Pick(instance)
    _spun.Value = 0f;           // the other object's Variable
```

```csharp
var spinner = SchematicsManager.Spawn(spinnerPrefab, position, rotation); // from the prefab's pool
int uid = spinner.UID;

ExecutionContext.Pick(uid).Disable<Spinner>();  // OnDisable, then skipped by every lifecycle method
ExecutionContext.Pick(uid).Enable();            // every Behavior on it
SchematicsManager.Destroy(spinner);             // OnDisable → OnDestroy, then back into the pool
```

### Signals
Signals are `SignalHandle` fields on a Behavior (no initializer needed; the editor generates and saves their assets), addressed to a spawned Schematic Object by its UID, the same ID that `ExecutionContext.Pick`, Variables and `GetSchematicBehavior` use. Behaviors and Schematic Graphs on the same object send to and hear each other.

```csharp
public class Health : SchematicBehavior
{
    [OutputSignal, Parameter("Amount", typeof(float))]
    public readonly SignalHandle<float> Damaged;
    public readonly VariableHandle<float> Current = 100f;

    public void TakeDamage(float amount)
    {
        Current.Value -= amount;
        Damaged.Invoke(amount);              // to the selected instance's object
    }
}

public class HitFlash : SchematicBehavior
{
    private readonly VariableHandle<float> _flash = 0f;

    protected override void OnStart()
    {
        // The handler runs with this HitFlash instance as Self, so _flash is this object's value.
        Self.GetSchematicBehavior<Health>().Damaged.Subscribe(amount => _flash.Value = 1f);
    }
}
```

Each declared Signal has a Signal asset inside the Behavior's Metadata, shown under the Behavior in the Inspector; drag it into a Schematic Graph to make **Send a Signal** / **On Signal Received** nodes. Graph nodes send and listen under the UID of the object running the graph. Subscriptions made in a lifecycle method are released when the listener or the object it listens to despawns, so subscribe in `OnStart`.

To send a Signal to everyone listening, from anywhere (input, managers, tools), use `signal.Broadcast(value)`. The value can be any type: listeners that take another type get it converted (or their type's default), listeners that take `object` get it as is, and `signal.BroadcastDefault()` sends every listener its default (e.g. when an input is released).

`GetSchematicBehavior<T>()` is the Behavior counterpart to `GetComponent`: it returns the Behavior with that object's instance selected, so its Variables act on that object, or null if the Behavior doesn't control it.

```csharp
var controller = player.GetSchematicBehavior<PhysicsBasedCharacterController>();
if (controller != null)
    Debug.Log(controller.Properties.Value.MoveSpeed);
```

Inside a Behavior, reach the other Behaviors on your own GameObject through `BehaviorHandle<T>` fields instead. Like Signals they need no initializer (they're bound when the Behavior is created), and every read is an array index: no lookup by GameObject, no type checks, no allocation, so they're the way to talk to siblings every frame.

```csharp
[RequireBehavior(typeof(CharacterMotionContext), typeof(CharacterRaycastContext))]
public class Glider : SchematicBehavior
{
    private readonly BehaviorHandle<CharacterMotionContext> _motion;
    private readonly BehaviorHandle<CharacterRaycastContext> _raycast;

    protected override void OnFixedUpdate()
    {
        var motion = _motion.Selected;           // this object's Motion Context, selected (null if it has none)
        var raycastRow = _raycast.Instance;      // this object's row in the Raycast Context, selecting nothing
        if (motion == null || raycastRow == null)
            return;

        if (!_raycast.Behavior.IsGrounded[raycastRow])
            motion.ApplyVerticalForce(-1f);
    }
}
```

`_motion.Select(instance)` and `_motion[instance]` do the same for an instance you hold (in a Signal handler, say); `_motion.Has(instance)` tells whether the object has one, and `_motion.Behavior` is the shared system with nothing selected. A sibling's rows belong to its own Behavior, so index its Variables with `_raycast[instance]`, never with your own instance (that throws). Siblings that aren't spawned read as null, and Behaviors added at runtime with `AddBehavior` show up at once. A base type finds a derived Behavior, as `GetSchematicBehavior` does.

Lifecycle order: `OnCreate` (first creation only) → `OnStart` → `OnEnable` → `OnUpdate`/`OnLateUpdate`/`OnFixedUpdate` → `OnDisable` → `OnDestroy`.
Behaviors also receive `OnCollisionEnter`/`Exit` and `OnTriggerEnter`/`Exit` for the GameObject they're attached to. Variables reset on every spawn, so fetch components with `Self.GetComponent<T>()`, which caches them for the object's whole life.

`[RequireBehavior(typeof(Other))]` is the Behavior counterpart to `[RequireComponent]`: attaching the Behavior attaches the ones it needs too.

### The IO Dock
The Schematic Graph window's **Prefab** tab lists the prefab's GameObjects and the Behaviors on each, with their **Variables** (above) and **Signals**:

- Drag a **Variable** into the graph for a **Get** or **Set Variable** node.
- Drag a **Signal** for **Send a Signal** (a pin per parameter) or **On Signal Received**.
- Drag a **Behavior** for a **Behavior** node: every Variable and its Enabled state as pins, and optionally one of its public methods to call. The reflection is done once per Behavior type and cached.
- Drag a **GameObject** for a **Prefab Object** node (that object on whichever spawned copy runs the graph).
- Click a row to jump to the nodes that use it (click again for the next one); click a node to find its row.

Signal nodes only need the object's UID, since Signals reach the whole object. Variable and Behavior nodes also save which Behavior and which GameObject (by path) they mean, because those values live on one instance. The **Graph** tab lists the graph's Signal/Variable/Behavior nodes, and the minimap marks them with icons.

### State Machines
Attach the **State Machine** Behavior (Logic/State Machine) and click **Open State Machine**. States nest: being in *Grounded* also means being in its parent *Alive*.

- **State Manager**: a spreadsheet of states against the prefab's Behaviors. Click a cell to turn that Behavior on or off in that state; empty cells inherit from the parent state.
- **State Graph**: states and their transitions, one level at a time. Double-click a state to go inside it.
- **Transitions** read like an event sheet: *when [Signal] arrives* or *if [Variable] [≤] [value]* (plus time in state, or another Behavior being on), *→ go to [state]*. A parent's transitions apply in all of its children, and win over theirs.
- Each state can have its own Schematic Graph, which runs while the state is active (**On State Enter / Update / Exit** nodes, plus its Signal nodes).
- Send the State Machine's `SetState` Signal with a state name to change state from code or a graph; `OnStateChanged` reports each change.

### Singleton Graphs
Create one with **Create → Remedy Toolkit → Schematics → Singleton Schematic**. It doesn't need to be in a Resources folder: the editor registers every Singleton Graph automatically (in the Schematic Manager Data, which is in Resources). When the game starts, each one gets its own Schematic Object, named `[Singleton] <graph name>` and kept across scenes, which runs the graph like any other object: its events, Signals, Variable nodes and the game-wide events (scene loaded, runtime update, …).

Give it a State Machine from the graph window's side panel (**Create State Machine**). The definition is stored on the asset; the state lives on the object. Reach the object from code with `mySingletonGraph.Host` or `SingletonGraph.Find("Name")`.

### Function Graphs
A Function Graph is a routine any logic graph can call like a node. Create one with **Create → Remedy Toolkit → Schematics → Function Graph**, or from the Project Manager's Functions group.
- **Inputs and outputs:** set them in the IO Dock's Graph tab, under **Function**. Each has a name, a type and a default value. Number inputs also get a control (a slider, whole-number slider, toggle or dropdown, with its range or options).
- **On Called** starts the function. Its pins are the inputs. The function runs on the object calling it, so Self is the caller.
- **Return** hands the outputs back. The function can wait (a Wait node, a delay) before it, and the caller carries on once Return is reached. With no Return, a call carries straight on.
- **Call** is the node other graphs use. Its pins are the function's inputs and outputs, and any input that isn't wired can be set right on the node: numbers with their control, other types (vectors, colours, text, objects) with a value field. The same goes for Return's outputs. Drop a Function Graph into a graph to make one, and double-click its title to open the function. A function can call itself, up to 64 calls deep.
- **Variables** in a Function Graph belong to each call: every call starts with the defaults, and calls running at once (one waiting while another starts, or a function calling itself) each keep their own values.
- **Collapse to Function:** select some nodes in a logic graph, right-click and pick **Collapse to Function**. They move into a new Function Graph, and a Call node takes their place, wired the way they were. The flow coming in starts at its On Called, the flow going out leaves through its Return, values wired in become its inputs, and values wired out become its outputs. Some selections can't be collapsed, and a message says why: ones that include an event, ones the flow enters at more than one node, and ones where the flow leaves to different places (a function returns to one place).
- **In Library** (also under Function in the Graph tab) lists it in every logic graph's node search under Functions/(its Path). Its Description, Use For and Notes document it the way built-in nodes are documented, and the assistant learns it too.

Mesh Graphs can be called the same way, from other Mesh Graphs (see Features/Meshes/README.md).

Sound Cues can be called from other Sound Cues too, with a **Function** node (under Functions in a Sound Cue's node search, for cues marked In Library):
- The called cue's nodes become part of the calling cue's sound, on its time, so the call costs the same as having the nodes there. What reaches its Output, at its Volume, comes out of the Function node's Audio pin.
- A **Sound Input** node is sound the cue takes when it's called. It becomes a pin on the Function node, and can carry sound or any control (a curve, a parameter). When the cue plays on its own, its Preview pin stands in.
- Its **Parameters** become pins too, with the control set on each Parameter node in the Inspector (slider, toggle, dropdown...), set right on the Function node when nothing's wired in.
- This is different from the **Sound Cue** node, which plays another cue as its own sound (with its own length, delay and looping). A Function is part of the cue, so it can change the sound it's given: a radio-voice filter chain, a muffled-through-a-wall pass, an engine layer.
- Sound Cue math is one small node per operation: Add, Subtract, Multiply, Divide, Modulo, Power, Min, Max, and Sine Wave, Sin, Cos, Abs, Floor, Fraction, Square Root, Negate, One Minus, Clamp 0 to 1, Sign, Decibels To Volume and Volume To Decibels. Each is a flat square with no title bar. Its icon in the middle is its symbol (+, ×, 1−x…) or a plot of its own curve. Numbers for unwired pins are typed in beside them, outside the square, and the node's name shows on hover. Cues saved with the old all-in-one Math and Function nodes are switched to these when opened, keeping their wires and values.
- **Collapse to Function** works in Sound Cues as well. Wires coming in become Sound Inputs, and the cue's Parameters stay Parameters. The one sound going out goes to the new cue's Output. The new cue is Mono, since the cue calling it decides how it's heard.

The old **Call Function** and **On Function** nodes still work, but show a warning badge suggesting a Function Graph instead. Any node can show a warning: a yellow wavy outline and a **!** badge, with the warning shown when you hover over the badge. A Call node with no function set gets one too.



---

## 🏢 Professional Services

### Custom Development
Looking for a **Unity architecture specialist** who can:
- **Rescue legacy projects** from performance hell
- **Build scalable systems** that grow with your team  
- **Implement SOLID principles** in Unity environments
- **Create custom tooling** that matches (or at least does not conflict with) your workflow

### Consulting & Training  
- **Code reviews** and architecture audits
- **Team training** on advanced Unity patterns
- **Performance optimization** consulting
- **Custom framework development**

**Contact**: [TheChayed@Gmail.com]

---

## 📈 Why Choose The Remedy?

### For Solo Developers
- **Rapid prototyping** without technical debt
- **Professional architecture** from day one  
- **No performance compromises** as you scale

### For Teams  
- **Designer-friendly** visual interface
- **Programmer-approved** performance and architecture
- **Reduced onboarding time** with intuitive workflows

### For Enterprises
- **Maintainable codebases** that survive team changes
- **Modular architecture** supporting multiple projects
- **Performance guarantees** for shipped products

---

## 🔮 Roadmap

- [ ] **Schematic Singletons** - Global Schematic 
- [ ] **Timeline Control** - Interact with the Timeline with Nodes and call ScriptableEvents or vice versa
- [ ] **Enemy AI** - Communicate with the new Behavior tree package that Unity recently released for Enemy Behavior 
- [ ] **Level Streaming** - Editor tooling to allow designers to create level chunks in scene and stream them into play
- [ ] **Multiplayer Integration** - NetworkEvents for ScriptableEvents so they can optionally be synced via multiplayer, and also incorporate multiplayer in feature components
- [ ] **Analytics Integration** - Analytics functionality to track player behavior using the Schematic Graph
- [ ] **Asset Store Release** - Public availability
- [ ] **Documentation Portal** - Comprehensive guides and tutorials

---

## 📝 Documentation

- [**Getting Started Guide**](docs/getting-started.md)
- [**Architecture Overview**](docs/architecture.md) 
- [**Performance Deep Dive**](docs/performance.md)
- [**API Reference**](docs/api-reference.md)

---

## 🤝 Contributing

This toolkit represents years of solving Unity's core architectural problems. If you're interested in contributing to the future of Unity development, please:

1. **Fork the repository**
2. **Create a feature branch** (`git checkout -b feature/amazing-feature`)
3. **Commit your changes** (`git commit -m 'Add amazing feature'`)  
4. **Push to the branch** (`git push origin feature/amazing-feature`)
5. **Open a Pull Request**

All contributors whose Pull Requests are accepted will **receive a free commercial license key** for the Toolkit upon release, as thanks for helping shape its future.

---

## 📄 License

This project is **proprietary software**. All rights are reserved by Cairo Creative Studios, as designated by the creator of the Toolkit, Chad Wolfe.  

- The source code is provided for evaluation and contribution purposes only.  
- You may not copy, modify, or redistribute this project outside of submitting contributions through Pull Requests.  
- Commercial use requires a license. Inquiries can be made at **[TheChayed@Gmail.com]**.  
- Contributors with accepted Pull Requests will be granted a free commercial license key.  
- A commercial license key grants the right to **use the Remedy Toolkit in commercial projects**, but **does not grant redistribution or resale rights**.  

---

## ⭐ Support the Project

If The Remedy Toolkit has saved you development time and improved your Unity workflow, please:
- **Star this repository** ⭐
- **Share with your Unity community** 🔄  
- **Follow for updates** 👁️
- **Consider hiring me** for your next Unity project 💼

---

*"Don't just build games. Build them right."* - The Remedy Toolkit
