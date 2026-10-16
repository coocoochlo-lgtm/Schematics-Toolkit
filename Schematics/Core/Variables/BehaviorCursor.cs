/// <summary>
/// Which instance a Behavior is currently acting on. Shared by every <see cref="VariableHandle{T}"/> the Behavior
/// declared, so switching instances is one write here instead of a lookup in every handle.
/// </summary>
internal sealed class BehaviorCursor
{
    public BehaviorInstance Instance;
    // The selected instance's row in the Runtime, or -1 if nothing is selected.
    public int Row = -1;
    public BehaviorRuntime Runtime;

    public void Select(BehaviorInstance instance)
    {
        Instance = instance;
        Row = instance != null ? instance.Row : -1;
    }
}
