using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Builds the domain-settings catalog from <see cref="DomainSettingAttribute"/> on
/// <see cref="CacheOrchestratorOptions.DomainCacheSettings"/>.
/// </summary>
public static class DomainSettingCatalog
{
    private static readonly ConcurrentDictionary<bool, IReadOnlyList<DomainSettingCatalogEntry>> Cache = new();

    /// <summary>All attributed domain settings (config shape).</summary>
    public static IReadOnlyList<DomainSettingCatalogEntry> GetEntries() =>
        Cache.GetOrAdd(false, static _ => Build(overlayOnly: false));

    /// <summary>Only settings with <see cref="DomainSettingAttribute.RuntimeOverlay"/>.</summary>
    public static IReadOnlyList<DomainSettingCatalogEntry> GetOverlayEntries() =>
        Cache.GetOrAdd(true, static _ => Build(overlayOnly: true));

    /// <summary>Looks up an entry by camelCase <paramref name="id"/> (case-insensitive).</summary>
    public static DomainSettingCatalogEntry? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        foreach (DomainSettingCatalogEntry e in GetEntries())
        {
            if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.PropertyName, id, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        return null;
    }

    private static IReadOnlyList<DomainSettingCatalogEntry> Build(bool overlayOnly)
    {
        List<DomainSettingCatalogEntry> list = [];
        foreach (PropertyInfo prop in typeof(CacheOrchestratorOptions.DomainCacheSettings)
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            DomainSettingAttribute? attr = prop.GetCustomAttribute<DomainSettingAttribute>();
            if (attr is null)
                continue;
            if (overlayOnly && !attr.RuntimeOverlay)
                continue;

            Type t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            IReadOnlyList<string>? enumValues = null;
            if (attr.Kind == DomainSettingValueKind.Enum && t.IsEnum)
                enumValues = Enum.GetNames(t);

            list.Add(new DomainSettingCatalogEntry
            {
                Id = JsonNamingPolicy.CamelCase.ConvertName(prop.Name),
                PropertyName = prop.Name,
                DisplayName = attr.DisplayName ?? SplitDisplayName(prop.Name),
                Group = attr.Group,
                Kind = attr.Kind,
                RuntimeOverlay = attr.RuntimeOverlay,
                EnumValues = enumValues,
            });
        }

        list.Sort((a, b) =>
        {
            int g = string.Compare(a.Group ?? "", b.Group ?? "", StringComparison.OrdinalIgnoreCase);
            return g != 0 ? g : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static string SplitDisplayName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return propertyName;
        Span<char> buf = stackalloc char[propertyName.Length * 2];
        int n = 0;
        for (int i = 0; i < propertyName.Length; i++)
        {
            char c = propertyName[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(propertyName[i - 1]) || (i + 1 < propertyName.Length && char.IsLower(propertyName[i + 1]))))
                buf[n++] = ' ';
            buf[n++] = c;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(new string(buf[..n]).ToLowerInvariant());
    }
}
