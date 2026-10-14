// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace REDox.Csv;

public static class CsvSequence
{
    private const int MinimumBufferSize = 1024;

    public static async IAsyncEnumerable<T?> DeserializeAsync<T>(
        Stream stream,
        SerializerSettings? settings = null,
        CsvDocumentOptions options = default,
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
    ///     Parses CSV records from the stream one row at a time.
    /// </summary>
    /// <remarks>
    ///     The yielded <see cref="DElement" /> is backed by a document that is valid only until the next
    ///     iteration (MoveNextAsync) or disposal of the enumerator. Use <see cref="DElement.Clone" /> or convert
    ///     the value (e.g. <c>To&lt;T&gt;()</c>) if it must be retained beyond that scope.
    /// </remarks>
    public static async IAsyncEnumerable<DElement> ParseAsync(
        Stream stream,
        SerializerSettings? settings = null,
        CsvDocumentOptions options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        var buffer = ArrayPool<byte>.Shared.Rent(
            Math.Max(settings.DefaultBufferSize, MinimumBufferSize));
        var scanner = new Scanner();

        byte[]? headerBuffer = null;
        var headerLength = 0;

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

                while (true)
                {
                    var boundary = scanner.Scan(buffer, length, eof);

                    if (boundary < 0)
                    {
                        break;
                    }

                    var count = boundary + 1;

                    if (options.HasHeaderRecord && headerBuffer == null)
                    {
                        StoreHeader(buffer, count, settings, options, ref headerBuffer, ref headerLength);
                    }
                    else
                    {
                        using var document = ParseRecord(buffer, count, headerBuffer, headerLength, settings, options);

                        foreach (var element in document.RootElement.EnumerateArray())
                        {
                            yield return element;
                            break;
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    length = Compact(buffer, count, length, ref scanner);
                }

                if (eof && length > 0)
                {
                    if (options.HasHeaderRecord && headerBuffer == null)
                    {
                        StoreHeader(buffer, length, settings, options, ref headerBuffer, ref headerLength);
                    }
                    else
                    {
                        using var document = ParseRecord(buffer, length, headerBuffer, headerLength, settings, options);

                        foreach (var element in document.RootElement.EnumerateArray())
                        {
                            yield return element;
                            break;
                        }
                    }

                    length = 0;
                }
            }
        }
        finally
        {
            if (headerBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(headerBuffer);
            }

            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void StoreHeader(byte[] buffer, int count, SerializerSettings settings,
        CsvDocumentOptions options, ref byte[]? headerBuffer, ref int headerLength)
    {
        using var _ = CsvDocument.Parse(buffer.AsSpan(0, count), settings, options);

        headerBuffer = ArrayPool<byte>.Shared.Rent(count);
        buffer.AsSpan(0, count).CopyTo(headerBuffer);
        headerLength = count;
    }

    private static CsvDocument ParseRecord(byte[] buffer, int count, byte[]? headerBuffer, int headerLength,
        SerializerSettings settings, CsvDocumentOptions options)
    {
        if (headerBuffer == null)
        {
            return CsvDocument.Parse(buffer.AsSpan(0, count), settings, options);
        }

        var combined = ArrayPool<byte>.Shared.Rent(headerLength + count);

        try
        {
            headerBuffer.AsSpan(0, headerLength).CopyTo(combined);
            buffer.AsSpan(0, count).CopyTo(combined.AsSpan(headerLength));

            return CsvDocument.Parse(combined.AsSpan(0, headerLength + count), settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(combined);
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

    private struct Scanner
    {
        private bool _inQuotes;
        private int _position;

        public int Scan(byte[] buffer, int length, bool eof)
        {
            while (_position < length)
            {
                var c = buffer[_position];

                if (_inQuotes)
                {
                    if (c == '"')
                    {
                        if (_position + 1 >= length && !eof)
                        {
                            return -1;
                        }

                        if (_position + 1 < length && buffer[_position + 1] == '"')
                        {
                            _position += 2;
                            continue;
                        }

                        _inQuotes = false;
                    }

                    _position++;
                    continue;
                }

                if (c == '"')
                {
                    _inQuotes = true;
                    _position++;
                    continue;
                }

                if (c == '\n')
                {
                    return _position++;
                }

                if (c == '\r')
                {
                    if (_position + 1 >= length && !eof)
                    {
                        return -1;
                    }

                    if (_position + 1 < length && buffer[_position + 1] == '\n')
                    {
                        _position += 2;
                        return _position - 1;
                    }

                    return _position++;
                }

                _position++;
            }

            return -1;
        }

        public void Shift(int count)
        {
            _position -= count;

            if (_position < 0)
            {
                _position = 0;
            }
        }
    }
}