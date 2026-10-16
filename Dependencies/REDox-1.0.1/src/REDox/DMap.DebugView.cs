// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace REDox;

public sealed partial class DMap
{
    private string GetDebuggerDisplay()
    {
        return new DebugView(this).ToString();
    }

    private sealed class DebugView
    {
        private readonly DMap _map;

        public DebugView(DMap map)
        {
            _map = map;
        }

        public IReadOnlyDictionary<DValue, object>? Items
        {
            get
            {
                if (!_map.IsValid)
                {
                    return null;
                }

                var dict = new Dictionary<DValue, object>(_map.Count);

                foreach (var kv in _map)
                {
                    dict[kv.Key] = DValue.DebugView.ConvertValue(kv.Value);
                }

                return dict;
            }
        }

        public override string ToString()
        {
            return _map.IsValid ? $"DMap (Count={_map.Count})" : "DMap (Invalid)";
        }
    }
}