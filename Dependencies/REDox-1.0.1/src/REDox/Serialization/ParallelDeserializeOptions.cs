// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

public struct ParallelDeserializeOptions
{
    private const int DefaultMaxDegreeOfParallelism = 4;

    private const int DefaultMinimumNumberOfElements = 4;

    private const int DefaultMinimumNumberOfTokens = 1000;

    public bool ParallelDeserializeEnabled { get; init; }

    public int MaxDegreeOfParallelism
    {
        get
        {
            if (_maxDegreeOfParallelism == null)
            {
                return DefaultMaxDegreeOfParallelism;
            }

            return (int)_maxDegreeOfParallelism;
        }
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);

            _maxDegreeOfParallelism = value;
        }
    }

    public int MinimumNumberOfElements
    {
        get
        {
            if (_minimumNumberOfElements == null)
            {
                return DefaultMinimumNumberOfElements;
            }

            return (int)_minimumNumberOfElements;
        }
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);

            _minimumNumberOfElements = value;
        }
    }

    public int MinimumNumberOfTokens
    {
        get
        {
            if (_minimumNumberOfTokens == null)
            {
                return DefaultMinimumNumberOfTokens;
            }

            return (int)_minimumNumberOfTokens;
        }
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);

            _minimumNumberOfTokens = value;
        }
    }

    private readonly int? _maxDegreeOfParallelism;

    private readonly int? _minimumNumberOfElements;

    private readonly int? _minimumNumberOfTokens;
}