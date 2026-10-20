// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox;

public readonly partial struct DValue
{
    internal sealed class DebugView
    {
        private readonly DValue _value;

        public DebugView(DValue value)
        {
            _value = value;
        }

        public DToken Token => _value.GetToken();

        public DTokenType TokenType => _value.GetToken().Type;

        public DArray? AsArray =>
            _value.IsValid && _value.GetToken().Type == DTokenType.Array
                ? _value.AsArray()
                : null;

        public DMap? AsMap =>
            _value.IsValid && _value.GetToken().Type == DTokenType.Map
                ? _value.AsMap()
                : null;

        public DObject? AsObject =>
            _value.IsValid && _value.GetToken().Type == DTokenType.Map
                ? _value.AsObject()
                : null;

        public override string ToString()
        {
            if (!_value.IsValid)
            {
                return "DValue (Invalid)";
            }

            var token = _value.GetToken();

            if (token.IsContainer)
            {
                return $"DValue {token.Type} (Count = {_value.Count})";
            }

            if (!token.IsContainer)
            {
                if (token.Kind == DTokenKind.Null)
                {
                    return "DValue null";
                }

                if (token.Type == DTokenType.Text || token.Kind == DTokenKind.String)
                {
                    return $"DValue \"{_value.ToString()}\"";
                }

                return $"DValue {_value.ToString()}";
            }

            return $"DValue ({token})";
        }

        internal static object ConvertValue(DValue value)
        {
            return value.GetToken().Type switch
            {
                DTokenType.Array => value.AsArray(),
                DTokenType.Map => value.AsMap(),
                _ => (object)value
            };
        }
    }

    private string GetDebuggerDisplay()
    {
        return new DebugView(this).ToString();
    }
}