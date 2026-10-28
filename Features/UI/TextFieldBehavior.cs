using UnityEngine;
using Remedy.Schematics;
using UnityEngine.UIElements;

namespace Remedy.UI
{
    /// <summary>Exposes a named UI Builder TextField's changes and lets graph Signals update or clear its value.</summary>
    [SchematicBehaviorPath("UI/Text Field")]
    [SchematicUIElementBehavior(typeof(TextField))]
    public sealed class TextFieldBehavior : SchematicUIBehavior
    {
        [Tooltip("Sent whenever the text changes, with the new text.")]
        [OutputSignal, Parameter("Text", typeof(string))]
        public readonly SignalHandle<string> TextChanged;

        [Tooltip("Send text to put it in the field.")]
        [InputSignal, Parameter("Text", typeof(string))]
        public readonly SignalHandle<string> SetText;

        [Tooltip("Empties the field.")]
        [InputSignal]
        public readonly SignalHandle Clear;

        private TextField _textField;
        private BehaviorInstance _instance;
        private EventCallback<ChangeEvent<string>> _changeCallback;

        protected override void OnStart()
        {
            SetText.Subscribe(OnSetText);
            Clear.Subscribe(OnClear);
        }

        protected override void OnEnable()
        {
            _textField = Element as TextField;
            _instance = Self;
            if (_textField == null)
                return;

            _changeCallback = evt =>
            {
                if (_instance != null)
                    TextChanged.Invoke(_instance, evt.newValue);
            };
            _textField.RegisterValueChangedCallback(_changeCallback);
        }

        protected override void OnDisable()
        {
            if (_textField != null && _changeCallback != null)
                _textField.UnregisterValueChangedCallback(_changeCallback);
            _textField = null;
            _instance = null;
            _changeCallback = null;
        }

        private void OnSetText(string text)
        {
            if (_textField != null)
                _textField.value = text ?? string.Empty;
        }

        private void OnClear()
        {
            if (_textField != null)
                _textField.value = string.Empty;
        }
    }
}
