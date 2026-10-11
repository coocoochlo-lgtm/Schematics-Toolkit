// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace REDox.Cbor;

public static class CborSequence
{
    private const int MinimumBufferSize = 1024;

    public static async IAsyncEnumerable<T?> DeserializeAsync<T>(
        Stream stream,
        SerializerSettings? settings = null,
        CborDocumentOptions options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var element in ParseAsync(stream, settings, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return element.To<T>();
        }
    }

    /// <summary>
    ///     Parses a CBOR sequence (RFC 8742) from the stream.
    /// </summary>
    /// <remarks>
    ///     The yielded <see cref="DElement" /> is backed by a reused document and is valid only until the
    ///     next iteration (MoveNextAsync) or disposal of the enumerator. Use <see cref="DElement.Clone" /> or convert
    ///     the value (e.g. <c>To&lt;T&gt;()</c>) if it must be retained beyond that scope.
    /// </remarks>
    public static async IAsyncEnumerable<DElement> ParseAsync(
        Stream stream,
        SerializerSettings? settings = null,
        CborDocumentOptions options = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        // Each chunk of complete data items is parsed as a sequence, so the root is always an array.
        var sequenceOptions = options with { UseSequenceFormat = true };

        var buffer = ArrayPool<byte>.Shared.Rent(
            Math.Max(settings.DefaultBufferSize, MinimumBufferSize));
        var scanner = new Scanner();
        var cache = Helper.InstanceCache<CborDocument>.Get(() =>
            new CborDocument(SerializerSettings.Default));
        var document = cache.Value;

        try
        {
            var length = 0;
            var eof = false;

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

                scanner.Scan(buffer, length);

                // On invalid data, let the parser report the error at the offending position.
                var count = scanner.Invalid ? length : scanner.Boundary;

                if (count > 0)
                {
                    document.ParseInternal(buffer.AsMemory(0, count), settings, sequenceOptions);

                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        yield return element;

                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    length = Compact(buffer, count, length, ref scanner);
                }

                if (eof && length > 0)
                {
                    throw new InvalidDataException("The stream ended in the middle of a CBOR data item.");
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

    // Incrementally tracks CBOR data item boundaries so that partially received items are not re-scanned.
    private struct Scanner
    {
        private const long Indefinite = -1;

        private Stack<long>? _remaining;
        private long _skip;
        private int _position;

        public Scanner()
        {
        }

        public int Boundary { get; private set; }

        public bool Invalid { get; private set; }

        public void Scan(byte[] buffer, int length)
        {
            while (!Invalid)
            {
                if (_skip > 0)
                {
                    var take = (int)Math.Min(_skip, length - _position);
                    _position += take;
                    _skip -= take;

                    if (_skip > 0)
                    {
                        return;
                    }

                    CompleteItem();
                }

                if (_position >= length)
                {
                    return;
                }

                var initial = buffer[_position];
                var major = initial >> 5;
                var info = initial & 0x1f;

                var extra = info switch
                {
                    < 24 => 0,
                    24 => 1,
                    25 => 2,
                    26 => 4,
                    27 => 8,
                    31 => 0,
                    _ => -1
                };

                if (extra < 0)
                {
                    Invalid = true;
                    return;
                }

                if (_position + 1 + extra > length)
                {
                    return;
                }

                var value = info < 24 ? (ulong)info : ReadArgument(buffer, _position + 1, extra);
                _position += 1 + extra;

                if (initial == 0xff)
                {
                    if (_remaining is not { Count: > 0 } || _remaining.Peek() != Indefinite)
                    {
                        Invalid = true;
                        return;
                    }

                    _remaining.Pop();
                    CompleteItem();
                    continue;
                }

                switch ((CborMajorType)major)
                {
                    case CborMajorType.Binary:
                    case CborMajorType.String:
                        if (info == 31)
                        {
                            Push(Indefinite);
                        }
                        else if (value > int.MaxValue)
                        {
                            Invalid = true;
                            return;
                        }
                        else if (value == 0)
                        {
                            CompleteItem();
                        }
                        else
                        {
                            _skip = (long)value;
                        }

                        break;
                    case CborMajorType.Array:
                    case CborMajorType.Map:
                        if (info == 31)
                        {
                            Push(Indefinite);
                        }
                        else if (value > int.MaxValue)
                        {
                            Invalid = true;
                            return;
                        }
                        else if (value == 0)
                        {
                            CompleteItem();
                        }
                        else
                        {
                            Push((CborMajorType)major == CborMajorType.Map ? (long)value * 2 : (long)value);
                        }

                        break;
                    case CborMajorType.Tag:
                        // A tag wraps the following data item, which completes it.
                        break;
                    default:
                        if (info == 31)
                        {
                            Invalid = true;
                            return;
                        }

                        CompleteItem();
                        break;
                }
            }
        }

        public void Shift(int consumed)
        {
            _position -= consumed;
            Boundary -= consumed;
        }

        private void Push(long count)
        {
            (_remaining ??= new Stack<long>()).Push(count);
        }

        private void CompleteItem()
        {
            var remaining = _remaining;

            while (remaining is { Count: > 0 })
            {
                var top = remaining.Pop();

                if (top == Indefinite)
                {
                    remaining.Push(top);
                    return;
                }

                if (--top > 0)
                {
                    remaining.Push(top);
                    return;
                }
            }

            Boundary = _position;
        }

        private static ulong ReadArgument(byte[] buffer, int offset, int size)
        {
            var value = 0UL;

            for (var i = 0; i < size; i++)
            {
                value = (value << 8) | buffer[offset + i];
            }

            return value;
        }
    }
}