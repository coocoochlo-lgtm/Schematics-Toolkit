using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// The control a graph's Parameter shows on a node calling the graph as a function: a slider, a whole-number
    /// slider, a toggle, a dropdown or a number box, as the Parameter's <see cref="ParameterUI"/> says. Shared by every
    /// kind of graph's function node.
    /// </summary>
    public static class FunctionParameterControls
    {
        public static VisualElement Create(GraphParameter parameter, float value, Action<float> changed)
        {
            var ui = parameter.UI ?? new ParameterUI();
            float lo = Mathf.Min(ui.Min, ui.Max), hi = Mathf.Max(ui.Min, ui.Max);
            VisualElement field;
            switch (ui.Control)
            {
                case ParameterControl.Slider:
                {
                    var slider = new Slider(lo, hi) { value = Mathf.Clamp(value, lo, hi), showInputField = true };
                    slider.RegisterValueChangedCallback(e => changed(e.newValue));
                    field = slider;
                    break;
                }
                case ParameterControl.WholeNumber:
                {
                    var slider = new SliderInt(Mathf.RoundToInt(lo), Mathf.RoundToInt(hi)) { value = Mathf.RoundToInt(value), showInputField = true };
                    slider.RegisterValueChangedCallback(e => changed(e.newValue));
                    field = slider;
                    break;
                }
                case ParameterControl.Toggle:
                {
                    var toggle = new Toggle { value = value >= 0.5f };
                    toggle.RegisterValueChangedCallback(e => changed(e.newValue ? 1f : 0f));
                    field = toggle;
                    break;
                }
                case ParameterControl.Dropdown:
                {
                    var options = ui.OptionList.ToList();
                    if (options.Count == 0)
                        options.Add("(no options)");
                    int index = Mathf.Clamp(Mathf.RoundToInt(value), 0, options.Count - 1);
                    var dropdown = new DropdownField(options, index);
                    dropdown.RegisterValueChangedCallback(_ => changed(dropdown.index));
                    field = dropdown;
                    break;
                }
                default:
                {
                    var number = new FloatField { value = value };
                    number.RegisterValueChangedCallback(e => changed(e.newValue));
                    field = number;
                    break;
                }
            }
            field.tooltip = string.IsNullOrWhiteSpace(ui.Description) ? parameter.Name : ui.Description;
            field.style.minWidth = ui.Control == ParameterControl.Slider || ui.Control == ParameterControl.WholeNumber ? 140 : 50;
            field.style.flexGrow = 1;
            return field;
        }
    }
}
