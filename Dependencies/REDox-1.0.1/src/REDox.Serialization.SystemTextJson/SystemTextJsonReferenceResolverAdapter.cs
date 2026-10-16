// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Serialization.SystemTextJson;

sealed class SystemTextJsonReferenceResolverAdapter : ReferenceResolver<string>
{
    private readonly System.Text.Json.Serialization.ReferenceResolver _resolver;

    public SystemTextJsonReferenceResolverAdapter(System.Text.Json.Serialization.ReferenceResolver resolver)
    {
        _resolver = resolver;
    }

    protected override string GetReference(object value, out bool alreadyExists)
    {
        return _resolver.GetReference(value, out alreadyExists);
    }

    protected override void AddReference(string referenceId, object value)
    {
        _resolver.AddReference(referenceId, value);
    }

    protected override object? ResolveReference(string referenceId)
    {
        return _resolver.ResolveReference(referenceId);
    }

    protected override void WriteId(DataWriter writer, string referenceId)
    {
        writer.WriteSymbol(referenceId, SymbolKind.Reference);
    }

    protected override string ReadId(in DataReader reader, uint tokenId)
    {
        return reader.ReadString(tokenId);
    }
}