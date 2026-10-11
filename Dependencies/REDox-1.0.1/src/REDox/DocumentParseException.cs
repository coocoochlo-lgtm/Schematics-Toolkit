// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;

namespace REDox;

public abstract class DocumentParseException : Exception
{
    private const int MaxErrorValueSnippetLength = 128;
    private readonly long _bytePositionInLine;
    private readonly bool _hasPosition;
    private readonly long _lineNumber;

    private readonly string? _message;

    public DocumentParseException(string documentTypeName, long bytePosition)
    {
        DocumentTypeName = documentTypeName;
        BytePosition = bytePosition;
    }

    public DocumentParseException(string documentTypeName, long bytePosition, string message) : base(message)
    {
        DocumentTypeName = documentTypeName;
        _message = message;
        BytePosition = bytePosition;
    }

    public DocumentParseException(string documentTypeName, long bytePosition, string message, long lineNumber,
        long bytePositionInLine) : base(message)
    {
        DocumentTypeName = documentTypeName;
        _message = message;
        _lineNumber = lineNumber;
        _bytePositionInLine = bytePositionInLine;
        _hasPosition = true;
        BytePosition = bytePosition;
    }

    public override string Message => _message ?? $"{DocumentTypeName} ParseError ,BytePosition: {BytePosition}";

    public string DocumentTypeName { get; }

    public long BytePosition { get; }

    public virtual long LineNumber => _hasPosition ? _lineNumber : 0;

    public virtual long BytePositionInLine => _hasPosition ? _bytePositionInLine : BytePosition;

    protected static (long LineNumber, long BytePositionInLine) GetLinePosition(ReadOnlySpan<byte> bytes,
        long bytePosition)
    {
        var end = bytePosition < 0 ? 0 : (int)Math.Min(bytePosition, bytes.Length);
        var line = 1;
        var lineOffset = 0;
        var remaining = bytes.Slice(0, end);

        while (true)
        {
            var newline = remaining.IndexOf((byte)'\n');

            if (newline < 0)
            {
                break;
            }

            line++;
            lineOffset += newline + 1;
            remaining = remaining.Slice(newline + 1);
        }

        return (line, end - lineOffset);
    }

    protected static void AppendErrorValue(StringBuilder sb, ReadOnlySpan<byte> source, int offset, int length)
    {
        if (length < 0 || offset < 0 || offset >= source.Length)
        {
            return;
        }

        var available = Math.Min(length, source.Length - offset);
        var isTruncated = available > MaxErrorValueSnippetLength;
        var snippetLength = isTruncated ? MaxErrorValueSnippetLength : available;
        var value = Utf8Helper.GetUtf16String(source.Slice(offset, snippetLength));

        if (isTruncated)
        {
            value += "...";
        }

        sb.Append($" '{value}'");
    }
}