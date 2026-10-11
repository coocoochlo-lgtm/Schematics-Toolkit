// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace REDox.Json;

public static class JsonSequence
{
    private const int MinimumBufferSize = 1024;

    public static async IAsyncEnumerable<T?> DeserializeAsync<T>(
        Stream stream,
        SerializerSettings? settings = null,
        JsonDocumentOptions options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await foreach (var element in ParseAsync(stream, settings, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return element.To<T>();
        }
    }

    /// <summary>
    ///     Parses a sequence of JSON values from the stream.
    /// </summary>
    /// <remarks>
    ///     The yielded <see cref="DElement" /> is backed by a reused document and is valid only until the
    ///     next iteration (MoveNextAsync) or disposal of the enumerator. Use <see cref="DElement.Clone" /> or convert
    ///     the value (e.g. <c>To&lt;T&gt;()</c>) if it must be retained beyond that scope.
    /// </remarks>
    public static async IAsyncEnumerable<DElement> ParseAsync(
        Stream stream,
        SerializerSettings? settings = null,
        JsonDocumentOptions options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        // Encoding cannot be detected in the middle of a stream, so always treat the data as UTF8.
        var newlineDelimited = options.UseNewlineDelimitedFormat;

        var buffer = ArrayPool<byte>.Shared.Rent(
            Math.Max(settings.DefaultBufferSize, MinimumBufferSize));
        var scanner = new Scanner();
        var cache = Helper.InstanceCache<JsonDocument>.Get(() =>
            new JsonDocument(SerializerSettings.Default));
        var document = cache.Value;

        try
        {
            var length = 0;
            var eof = false;
            var bomChecked = false;

            while (!eof)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (length == buffer.Length)
                {
                    Grow(ref buffer, length);
                }

                var readBytes = await stream
                    .ReadAsync(buffer.AsMemory(length), cancellationToken)
                    .ConfigureAwait(false);

                if (readBytes == 0)
                {
                    eof = true;
                }
                else
                {
                    length += readBytes;
                }

                if (!bomChecked)
                {
                    if (length < 3 && !eof)
                    {
                        continue;
                    }

                    bomChecked = true;

                    if (buffer.AsSpan(0, length).StartsWith("\uFEFF"u8))
                    {
                        length = Compact(buffer, 3, length, ref scanner);
                    }
                }

                if (newlineDelimited)
                {
                    var boundary = scanner.ScanNewline(buffer, length);

                    if (boundary < 0)
                    {
                        if (!eof)
                        {
                            continue;
                        }

                        boundary = length - 1;
                    }

                    var count = boundary + 1 - scanner.Start;

                    if (count > 0 && !IsBlank(buffer, scanner.Start, count))
                    {
                        document.ParseInternal(buffer.AsMemory(scanner.Start, count), settings, options);

                        foreach (var element in document.RootElement.EnumerateArray())
                        {
                            yield return element;

                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    length = Compact(buffer, boundary + 1, length, ref scanner);
                    continue;
                }

                scanner.ScanArray(buffer, length);

                if (scanner.RootClosed)
                {
                    var count = scanner.CloseIndex + 1 - scanner.Start;

                    document.ParseInternal(buffer.AsMemory(scanner.Start, count), settings, options);

                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        yield return element;

                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    yield break;
                }

                if (scanner.RootIsArray && scanner.LastSeparator > scanner.Start)
                {
                    var separator = scanner.LastSeparator;
                    var count = separator + 1 - scanner.Start;

                    // Temporarily replace the last top level comma with ']' so that
                    // the whole buffer can be parsed as a single JsonDocument.
                    buffer[separator] = (byte)']';

                    document.ParseInternal(buffer.AsMemory(scanner.Start, count), settings, options);

                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        yield return element;

                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    // Reuse the replaced comma position as the '[' of the next segment.
                    buffer[separator] = (byte)'[';

                    length = Compact(buffer, separator, length, ref scanner);
                    continue;
                }

                if (eof)
                {
                    var count = length - scanner.Start;

                    if (count > 0 && !IsBlank(buffer, scanner.Start, count))
                    {
                        // A single non array root value, or a JSON with an invalid termination.
                        document.ParseInternal(buffer.AsMemory(scanner.Start, count), settings, options);

                        yield return document.RootElement;
                    }
                }
            }
        }
        finally
        {
            cache.Dispose();

            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void Grow(ref byte[] buffer, int length)
    {
        var next = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);

        Array.Copy(buffer, next, length);
        ArrayPool<byte>.Shared.Return(buffer);

        buffer = next;
    }

    private static int Compact(byte[] buffer, int consumed, int length, ref Scanner scanner)
    {
        var remaining = length - consumed;

        if (remaining > 0 && consumed > 0)
        {
            Array.Copy(buffer, consumed, buffer, 0, remaining);
        }

        scanner.Shift(consumed);

        return remaining;
    }

    private static bool IsBlank(byte[] buffer, int offset, int count)
    {
        for (var i = offset; i < offset + count; i++)
        {
            if (buffer[i] > 0x20)
            {
                return false;
            }
        }

        return true;
    }

    private struct Scanner
    {
        private int _depth;
        private bool _escape;
        private bool _inString;
        private int _position;
        private bool _rootIsSingle;

        public Scanner()
        {
            LastSeparator = -1;
            CloseIndex = -1;
        }

        public int Start { get; private set; }

        public int LastSeparator { get; private set; }

        public int CloseIndex { get; private set; }

        public bool RootIsArray { get; private set; }

        public bool RootClosed { get; private set; }

        public int ScanNewline(byte[] buffer, int length)
        {
            var boundary = -1;

            while (_position < length)
            {
                if (buffer[_position] == (byte)'\n')
                {
                    boundary = _position;
                }

                _position++;
            }

            return boundary;
        }

        public void ScanArray(byte[] buffer, int length)
        {
            while (_position < length)
            {
                var c = buffer[_position];

                if (_inString)
                {
                    if (_escape)
                    {
                        _escape = false;
                    }
                    else if (c == (byte)'\\')
                    {
                        _escape = true;
                    }
                    else if (c == (byte)'"')
                    {
                        _inString = false;
                    }

                    _position++;
                    continue;
                }

                if (!RootIsArray && !_rootIsSingle)
                {
                    if (c <= 0x20)
                    {
                        _position++;
                        Start = _position;
                        continue;
                    }

                    if (c == (byte)'[')
                    {
                        RootIsArray = true;
                        Start = _position;
                    }
                    else
                    {
                        _rootIsSingle = true;
                        Start = _position;
                    }

                    _position++;
                    continue;
                }

                switch (c)
                {
                    case (byte)'"':
                        _inString = true;
                        break;
                    case (byte)'[':
                    case (byte)'{':
                        _depth++;
                        break;
                    case (byte)']':
                        if (RootIsArray && _depth == 0)
                        {
                            CloseIndex = _position;
                            RootClosed = true;
                        }
                        else
                        {
                            _depth--;
                        }

                        break;
                    case (byte)'}':
                        _depth--;
                        break;
                    case (byte)',':
                        if (RootIsArray && _depth == 0)
                        {
                            LastSeparator = _position;
                        }

                        break;
                }

                _position++;

                if (RootClosed)
                {
                    return;
                }
            }
        }

        public void Shift(int offset)
        {
            Start = Math.Max(Start - offset, 0);
            _position = Math.Max(_position - offset, 0);
            LastSeparator = -1;
            CloseIndex = -1;
        }
    }
}