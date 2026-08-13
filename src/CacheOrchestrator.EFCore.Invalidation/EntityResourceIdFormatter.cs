using CacheOrchestrator.Configuration;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Globalization;
using System.Text;

namespace CacheOrchestrator.EFCore;

/// <summary>Formats an EF primary key as a CacheOrchestrator resource id.</summary>
internal static class EntityResourceIdFormatter
{
    /// <summary>
    /// Stringifies PK parts with invariant culture, joins composite keys with <c>:</c>,
    /// then <see cref="DomainName.NormalizeResourceId"/>.
    /// </summary>
    public static string? TryFormat(EntityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        IKey? key = entry.Metadata.FindPrimaryKey();
        if (key is null || key.Properties.Count == 0)
            return null;

        if (key.Properties.Count == 1)
        {
            object? value = entry.Property(key.Properties[0]).CurrentValue;
            return NormalizePart(value);
        }

        StringBuilder raw = new();
        for (int i = 0; i < key.Properties.Count; i++)
        {
            object? value = entry.Property(key.Properties[i]).CurrentValue;
            string? part = FormatPart(value);
            if (part is null)
                return null;

            if (i > 0)
                raw.Append(':');
            raw.Append(part);
        }

        string normalized = DomainName.NormalizeResourceId(raw.ToString());
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? NormalizePart(object? value)
    {
        string? formatted = FormatPart(value);
        if (formatted is null)
            return null;

        string normalized = DomainName.NormalizeResourceId(formatted);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? FormatPart(object? value)
    {
        if (value is null)
            return null;

        if (value is byte[] bytes)
            return Convert.ToHexString(bytes);

        string? text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
