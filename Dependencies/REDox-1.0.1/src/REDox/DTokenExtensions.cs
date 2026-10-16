// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace REDox;

public static class DTokenExtensions
{
    extension(DTokenType type)
    {
        public bool IsNumeric
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)type >> 1 == 1;
        }

        public bool IsStringEncoded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)type >> 1 == 2;
        }

        public bool IsContainer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)type >> 1 == 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)((int)type << 4);
        }
    }

    extension(DTokenKind kind)
    {
        public bool IsIgnore
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)kind >> 1 == 0;
        }

        public bool IsNumeric
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)kind >> 2 == 1;
        }

        public bool IsStringEncoded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)kind >> 2 == 2;
        }

        public bool IsContainer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)kind >> 2 == 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenType ToType()
        {
            return (DTokenType)((int)kind >> 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)((int)kind << 3);
        }
    }

    extension(DTokenVariant variant)
    {
        internal bool IsInherit
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((int)variant & 7) == 7;
        }

        public bool IsIgnore
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)variant >> 4 == 0;
        }

        public bool IsLiteral
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)variant >> 4 == 1;
        }

        public bool IsNumber
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)variant >> 5 == 1;
        }

        public bool IsStringEncoded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)variant >> 5 == 2;
        }

        public bool IsContainer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)variant >> 5 == 3;
        }

        internal DTokenVariant ToDefault()
        {
            return (DTokenVariant)((int)variant & ~7);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenType ToType()
        {
            return (DTokenType)((int)variant >> 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenKind ToKind()
        {
            return (DTokenKind)((int)variant >> 3);
        }
    }

    extension(TriviaKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.Trivia << 3) | (int)kind);
        }
    }

    extension(StringKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.String << 3) | (int)kind);
        }
    }

    extension(SymbolKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.Symbol << 3) | (int)kind);
        }
    }

    extension(IntegerKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.Integer << 3) | (int)kind);
        }
    }

    extension(FloatKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.Float << 3) | (int)kind);
        }
    }

    extension(BooleanKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.Boolean << 3) | (int)kind);
        }
    }

    extension(BigNumberKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.BigNumber << 3) | (int)kind);
        }
    }

    extension(ByteStringKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.ByteString << 3) | (int)kind);
        }
    }

    extension(TimestampKind kind)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DTokenVariant ToVariant()
        {
            return (DTokenVariant)(((int)DTokenKind.Timestamp << 3) | (int)kind);
        }
    }
}