// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;

namespace REDox;

public readonly struct DTriviaCollection
{
    private readonly DElement _element;

    internal DTriviaCollection(DElement element)
    {
        _element = element;
    }

    public bool IsValid => _element.IsValid;

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(nameof(DTriviaCollection));
        }
    }

    public void Clear()
    {
        ThrowIfInvalid();

        using var enumerator = GetEnumerator();

        if (enumerator.MoveNext())
        {
            var list = _element.Document.GetElementTriviaList(_element.Id);
            list.Clear();
        }
    }

    public void AddFirst(string value, TriviaKind kind = TriviaKind.Default)
    {
        ThrowIfInvalid();

        var list = _element.Document.GetElementTriviaList(_element.Id);

        list.Insert(0, _element.Document.JoinTrivia(0, Encoding.UTF8.GetBytes(value), kind));
    }

    public void AddLast(string value, TriviaKind kind = TriviaKind.Default)
    {
        ThrowIfInvalid();

        var list = _element.Document.GetElementTriviaList(_element.Id);

        list.Add(_element.Document.JoinTrivia(0, Encoding.UTF8.GetBytes(value), kind));
    }

    public Document.TriviaEnumerator GetEnumerator()
    {
        ThrowIfInvalid();

        return new Document.TriviaEnumerator(_element.Document, _element.Id);
    }
}