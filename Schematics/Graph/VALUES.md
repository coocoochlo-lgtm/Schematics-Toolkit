# How graph values work

Where a node input gets its value from, what is cached, and why. The code is in `SchematicGraphNode.cs`
(value plumbing), `PortValues.cs` (type conversion) and `Flow/Flow.cs` (the per-step stamp).

## Reading an input: `GetInputValue(name, field)`

| What's on the input | What it reads |
|---|---|
| A value handed to the flow by another peer (a replicated node) | That value. |
| A **query node** (no flow arrows: Comparison, Is Null, Find Game Objects With Tag, Vector 3, casts...) | Its `OnRequestValue`, run now. The result is remembered for the rest of the **flow step**, so several inputs (or nodes) reading it in one step compute it once. The next step computes it again. |
| An **action or event node** (has flow arrows) | The value it last wrote with `SetOutputValue` **for the object running the graph**: this flow's value first (so a For Each's children each see their own item), then that object's last value from an earlier flow. If it never wrote one for this object, its `OnRequestValue` is asked. Nodes whose outputs can be worked out any time (Get Variable, Get Graph Variable) set `AnswersOnRequest` and are read like queries. |
| Nothing | The `field` you passed: the inline value shown on the node, read fresh every time. Editing it in the graph (even in Play Mode) takes effect on the next read. |

Always pass the node's field as the default (`GetInputValue("Position", NewPosition)`). The overload without a default
reads the field by reflection, which works but boxes. Read into locals; don't write connected values back into the
field, or the inline value on the node gets replaced by whatever was last connected.

A connected value that can't be converted to the type asked for counts as "no value", and the default is used.

`TryGetInputValue(name, out value)` tells you whether a connected value was found. `GetInputValues<T>(name)` returns
every connected value (the list is reused by the node's next call; copy it to keep it).

## Conversions (`PortValues`)

Values travel as a `Union` (written by action nodes) or a plain object (returned by queries). `PortValues` checks what
the value really is before converting:

- numbers to numbers (`int`, `float`, `bool`, `IntTag`, `LayerMask`, enums), vectors to vectors (`Vector2/3/4`,
  `Color`, `Quaternion`). A number never becomes a vector.
- references by type: a component becomes its `GameObject`, a `GameObject` gives its component of the wanted type.
  A number never becomes a reference.
- lists of one kind to another (copies), anything to `string`, anything to `Union`, a tag name to an `IntTag`.

Common cases don't allocate.

## Writing an output: `SetOutputValue(name, value)`

The value is stored in the running flow (for the nodes after this one) and as the running object's latest value.
Graph assets are shared by every object running them, so written values are kept **per object** (destroyed objects are
dropped now and then). Queries normally `return` their value from `OnRequestValue` rather than writing it.

## What is cached

Only the wiring, never values: for each input, the outputs feeding it; output indices; the action nodes each node
leads to; whether a node is a query. It's rebuilt when the node is validated or marked `IsDirty`, the first time it's
needed (so builds work without the editor having run anything), and in the editor whenever a port's connections
change. The per-step memo of query results is keyed by a stamp that changes every flow step, so it can't go stale.

## Why it used to break

- Unconnected inputs read a cached "value" that was really the input's index (`_cachedInputsByName[port] = inputIndex`).
  `Union.GetValue<Vector3>()` reads raw memory, so input #1 read as `(1, 0, 0)`, which is why Set Position's
  Position always defaulted to `(1, 0, 0)`. Asking the same Union for a `GameObject` reinterpreted the number's bits
  as an object reference.
- The default you passed (the inline value) was ignored, and once a connected value had been pulled it stayed in the
  cache (which was also serialized into the graph), so inline edits never applied.
- Nothing ever called `OnRequestValue`, so query and condition nodes never produced a value.
