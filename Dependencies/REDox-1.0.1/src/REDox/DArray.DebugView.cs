// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace REDox;

public sealed partial class DArray
{
    private string GetDebuggerDisplay()
    {
        return new DebugView(this).ToString();
    }

    private sealed class DebugView
    {
        private readonly DArray _array;

        public DebugView(DArray array)
        {
            _array = array;
        }

        public IReadOnlyList<object>? Items
        {
            get
            {
                if (!_array.IsValid)
                {
                    return null;
                }

                var list = new List<object>(_array.Count);

                for (var i = 0; i < _array.Count; i++)
                {
                    list.Add(DValue.DebugView.ConvertValue(_array[i]));
                }

                return list;
            }
        }

        public override string ToString()
        {
            return _array.IsValid ? $"DArray (Count={_array.Count})" : "DArray (Invalid)";
        }
    }
}