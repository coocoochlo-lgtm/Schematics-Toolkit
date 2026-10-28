using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The nodes' gloss: a crisp shine over the upper part of a surface and a soft shade along its bottom, laid over it
/// (over the body gradient that gives the colour its depth). The math nodes' squares wear it.
/// </summary>
internal static class NodeGloss
{
    /// <summary>How strong the shine and the shade are on a coloured surface (the math squares).</summary>
    public const float Shine = 0.22f, Shade = 0.12f;

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

        // Over everything on the surface (they take no clicks).
        surface.Add(top);
        surface.Add(bottom);
    }
}
