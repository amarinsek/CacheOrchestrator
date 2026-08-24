using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Builds the domain-settings catalog from <see cref="DomainSettingAttribute"/> on
/// <see cref="CacheOrchestratorOptions.DomainCacheSettings"/> (including nested sections).
/// </summary>
public static class DomainSettingCatalog
{
    private static readonly ConcurrentDictionary<bool, IReadOnlyList<DomainSettingCatalogEntry>> Cache = new();

    private static readonly HashSet<Type> NestedSectionTypes =
    [
        typeof(DomainDataCacheSettings),
        typeof(DomainOutputCacheSettings),
        typeof(DomainClientCacheSettings),
        typeof(DomainFusionCacheSettings),
    ];

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
        Walk(typeof(CacheOrchestratorOptions.DomainCacheSettings), prefixId: null, prefixProperty: null, overlayOnly, list);

        list.Sort((a, b) =>
        {
            int g = string.Compare(a.Group ?? "", b.Group ?? "", StringComparison.OrdinalIgnoreCase);
            return g != 0 ? g : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static void Walk(
        Type type,
        string? prefixId,
        string? prefixProperty,
        bool overlayOnly,
        List<DomainSettingCatalogEntry> list)
    {
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            Type propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            string camel = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            string id = prefixId is null ? camel : prefixId + "." + camel;
            string propertyName = prefixProperty is null ? prop.Name : prefixProperty + "." + prop.Name;

            if (NestedSectionTypes.Contains(propType))
            {
                Walk(propType, id, propertyName, overlayOnly, list);
                continue;
            }

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
                Id = id,
                PropertyName = propertyName,
                DisplayName = attr.DisplayName ?? SplitDisplayName(prop.Name),
                Group = attr.Group,
                Kind = attr.Kind,
                RuntimeOverlay = attr.RuntimeOverlay,
                EnumValues = enumValues,
            });
        }
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
