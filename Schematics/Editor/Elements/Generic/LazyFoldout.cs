using System;
using System.Diagnostics;
using UnityEngine.UIElements;

/// <summary>
/// A foldout that can render its content lazily when expanded, or accept pre-built content.
/// Useful for expensive rendering operations that should only happen when the user expands the foldout.
/// </summary>
public class LazyFoldout : Foldout
{
    protected Func<VisualElement> _contentFactory;
    private bool _hasRendered = false;
    protected bool _isLazy = false;

    /// <summary>
    /// Creates a lazy foldout that will call the factory function when first expanded
    /// </summary>
    public LazyFoldout(Func<VisualElement> contentFactory = null, bool startExpanded = false)
    {
        _isLazy = contentFactory != null;
        _contentFactory = contentFactory;

        value = startExpanded;

        this.RegisterValueChangedCallback(OnFoldoutToggled);
        // The arrow (a custom image, which Unity doesn't swap for an open one) turns to show it's open.
        this.RegisterValueChangedCallback(evt => { if (evt.target == this) UpdateArrow(); });
        RegisterCallback<AttachToPanelEvent>(_ => UpdateArrow());

        if (startExpanded && _isLazy)
        {
            RenderContent();
        }
    }

    protected void OnFoldoutToggled(ChangeEvent<bool> evt)
    {
        if (evt.newValue && _isLazy && !_hasRendered)
        {
            RenderContent();
        }
    }

    /// <summary>Opens or closes it without the change event (the arrow turns all the same).</summary>
    public new void SetValueWithoutNotify(bool newValue)
    {
        base.SetValueWithoutNotify(newValue);
        UpdateArrow();
    }

    // Its own arrow (the first in its header: the header can hold other things' too) pointing down when open.
    private void UpdateArrow()
    {
        var header = this.Q<Toggle>(className: Foldout.toggleUssClassName);
        var arrow = header?.Q(name: "unity-checkmark");
        if (arrow != null)
            arrow.style.rotate = new Rotate(new Angle(value ? 90f : 0f));
    }

    public void RenderContent(bool force = false)
    {
        if ((_hasRendered || _contentFactory == null) && !force)
            return;

        var content = _contentFactory.Invoke();
        if (content != null)
        {
            Clear();
            Add(content);
        }

        _hasRendered = true;
    }
}
