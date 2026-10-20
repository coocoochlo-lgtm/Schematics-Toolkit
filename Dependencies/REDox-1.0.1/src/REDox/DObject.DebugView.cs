// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace REDox;

public sealed partial class DObject
{
    private string GetDebuggerDisplay()
    {
        return new DebugView(this).ToString();
    }

    private sealed class DebugView
    {
        private readonly DObject _object;

        public DebugView(DObject obj)
        {
            _object = obj;
        }

        public IReadOnlyDictionary<string, object>? Items
        {
            get
            {
                if (!_object.IsValid)
                {
                    return null;
                }

                var dict = new Dictionary<string, object>(_object.Count, StringComparer.Ordinal);

                foreach (var kv in _object)
                {
                    dict[kv.Key] = DValue.DebugView.ConvertValue(kv.Value);
                }

                return dict;
            }
        }

        public override string ToString()
        {
            return _object.IsValid ? $"DObject (Count={_object.Count})" : "DObject (Invalid)";
        }
    }
}