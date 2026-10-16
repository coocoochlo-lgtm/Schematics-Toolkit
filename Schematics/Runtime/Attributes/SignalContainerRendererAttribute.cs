using System;

/// <summary>
/// Draws a Signal container in the Schematic Editor's IO Dock for a <see cref="SignalData"/> field: the Signal's
/// name (editable), and its parameters, each with a name and a type picked from a dropdown. The Signal asset is made
/// for the field the first time it's drawn (saved inside the asset that owns the field) and assigned to it.
/// Parameters declared in code with <c>[Parameter]</c> on the field are fixed; otherwise the flags below say what
/// the user may change.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class SignalContainerRendererAttribute : CustomFieldRendererAttribute
{
    public Type DefaultType;
    /// <summary>The name a new Signal gets: the name of a member of the field's owner that returns it, or the name itself.</summary>
    public string DefaultName;
    /// <summary>Parameters can be added and removed.</summary>
    public bool ParametersEditable;
    /// <summary>Each parameter's type can be picked.</summary>
    public bool ParamTypeModifiable;
    /// <summary>Each parameter can be renamed.</summary>
    public bool ParamNameModifiable;
    /// <summary>The most parameters the Signal may have (Signals carry up to 10, or none).</summary>
    public int MaxParameters = 10;
    /// <summary>
    /// The code sends this Signal and graphs react to it (an Output, like Remedy Input's), so dragging it into a graph
    /// makes On Received. False: graphs send it, and the code listens.
    /// </summary>
    public bool Outgoing;
    public SignalData IOBase;

    public SignalContainerRendererAttribute(Type defaultType, string defaultName, bool parametersEditable, bool paramTypeModifiable = false, bool paramNameModifiable = false)
    {
        DefaultType = defaultType;
        DefaultName = defaultName;

        ParametersEditable = parametersEditable;
        ParamTypeModifiable = paramTypeModifiable;
        ParamNameModifiable = paramNameModifiable;
    }
}
