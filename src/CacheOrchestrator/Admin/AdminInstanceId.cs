using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Admin;

/// <summary>Resolves the Local Admin instance identifier.</summary>
internal static class AdminInstanceId
{
    public static string Resolve(CacheOrchestratorOptions.AdminOptions admin)
    {
        ArgumentNullException.ThrowIfNull(admin);
        if (!string.IsNullOrWhiteSpace(admin.InstanceId))
            return admin.InstanceId.Trim();

        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return "unknown";
        }
    }
}
