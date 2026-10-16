// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox;

public abstract partial class Document
{
    private string GetDebuggerDisplay()
    {
        return new DebugView(this).ToString();
    }

    private sealed class DebugView
    {
        private readonly Document _document;

        public DebugView(Document document)
        {
            _document = document;
        }

        public bool IsValid => _document.IsValid;

        public int TokenCount => _document.IsValid ? Math.Max(_document.GetTokens().Length - 1, 0) : 0;

        public DElement RootElement => _document.RootElement;

        public override string ToString()
        {
            var typeName = _document.GetType().Name;

            if (!_document.IsValid)
            {
                return $"{typeName} (Invalid)";
            }

            return $"{typeName} (Root = {_document.RootElement.Token.Type}, TokenCount = {TokenCount})";
        }
    }
}