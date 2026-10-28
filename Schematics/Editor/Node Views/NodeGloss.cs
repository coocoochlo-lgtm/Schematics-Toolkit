using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The nodes' gloss: a crisp shine over the upper part of a surface and a soft shade along its bottom, drawn behind
/// what's on it. Math nodes' squares and every node's body wear it.
/// </summary>
internal static class NodeGloss
{
    /// <summary>How strong the shine and the shade are on a coloured surface (the math squares).</summary>
    public const float Shine = 0.22f, Shade = 0.12f;

    /// <summary>On a dark node body: a little softer, so its fields stay easy to read.</summary>
    public const float BodyShine = 0.10f, BodyShade = 0.14f;

    /// <summary>The body's own colour under the gloss (what its old gradient averaged out to).</summary>
    public static readonly Color BodyColor = new(0.11f, 0.115f, 0.13f, 0.62f);

    /// <summary>
    /// Puts the gloss on a surface (again, if it's already on: replaced). <paramref name="radius"/> is the surface's
    /// corner rounding, so the shade follows its bottom corners.
    /// </summary>
    public static void Apply(VisualElement surface, float radius, float shine = Shine, float shade = Shade)
    {
        if (surface == null)
            return;
        surface.Q("gloss")?.RemoveFromHierarchy();
        surface.Q("gloss-shade")?.RemoveFromHierarchy();

        var top = new VisualElement { name = "gloss", pickingMode = PickingMode.Ignore };
        top.style.position = Position.Absolute;
        top.style.left = top.style.right = 3;
        top.style.top = 2;
        top.style.height = new Length(42, LengthUnit.Percent);
        top.style.borderTopLeftRadius = top.style.borderTopRightRadius = Mathf.Max(0f, radius - 2f);
        top.style.borderBottomLeftRadius = top.style.borderBottomRightRadius = 14;
        top.style.backgroundColor = new Color(1f, 1f, 1f, shine);

        var bottom = new VisualElement { name = "gloss-shade", pickingMode = PickingMode.Ignore };
        bottom.style.position = Position.Absolute;
        bottom.style.left = bottom.style.right = bottom.style.bottom = 0;
        bottom.style.height = new Length(30, LengthUnit.Percent);
        bottom.style.borderBottomLeftRadius = bottom.style.borderBottomRightRadius = Mathf.Max(0f, radius - 1f);
        bottom.style.backgroundColor = new Color(0f, 0f, 0f, shade);

        // Behind everything on the surface.
        surface.Insert(0, bottom);
        surface.Insert(0, top);
    }
}
