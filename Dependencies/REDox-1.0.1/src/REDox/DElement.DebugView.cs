// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox;

public readonly partial struct DElement
{
    private sealed class DebugView
    {
        private readonly DElement _element;

        public DebugView(DElement element)
        {
            _element = element;
        }

        public uint Id => _element.Id;

        public DToken? Token => _element.IsValid ? _element.Token : null;

        public DTokenType? TokenType => _element.IsValid ? _element.Token.Type : null;

        public DObject? AsObject =>
            _element.IsValid && _element.Token.Type == DTokenType.Map
                ? _element.AsObject()
                : null;

        public DMap? AsMap =>
            _element.IsValid && _element.Token.Type == DTokenType.Map
                ? _element.AsMap()
                : null;

        public DArray? AsArray =>
            _element.IsValid && _element.Token.Type == DTokenType.Array
                ? _element.AsArray()
                : null;

        public DValue AsValue =>
            _element.IsValid && !_element.Token.IsContainer
                ? _element.AsValue()
                : DValue.Null;

        public override string ToString()
        {
            if (!_element.IsValid)
            {
                return "DElement (Invalid)";
            }

            var token = _element.Token;

            if (token.IsContainer)
            {
                return $"DElement {token.Type} (Count = {_element.GetValueCount()})";
            }

            if (!token.IsContainer)
            {
                if (token.Kind == DTokenKind.Null)
                {
                    return "DElement null";
                }

                if (token.Type == DTokenType.Text || token.Kind == DTokenKind.String)
                {
                    return $"DElement \"{_element.ToString()}\"";
                }

                return $"DElement {_element.ToString()}";
            }

            return $"DElement ({token})";
        }
    }

    private string GetDebuggerDisplay()
    {
        return new DebugView(this).ToString();
    }
}