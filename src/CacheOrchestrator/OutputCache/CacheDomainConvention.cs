using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace CacheOrchestrator.OutputCache;

/// <summary>
/// MVC application model convention that turns <see cref="CacheDomainAttribute"/> into a
/// <see cref="DomainOutputCachePolicy"/> filter on each matching action.
/// </summary>
/// <remarks>
/// Action-level attributes override controller-level attributes. Registered automatically by
/// <c>AddCacheOrchestrator</c>.
/// </remarks>
internal sealed class CacheDomainConvention : IApplicationModelConvention
{
    /// <summary>
    /// Scans controllers and actions and attaches <see cref="DomainOutputCachePolicy"/> where a domain is declared.
    /// </summary>
    /// <param name="application">The MVC application model to update.</param>
    public void Apply(ApplicationModel application)
    {
        foreach (ControllerModel controller in application.Controllers)
        {
            CacheDomainAttribute? controllerAttr = controller.Attributes
                .OfType<CacheDomainAttribute>()
                .LastOrDefault();

            foreach (ActionModel action in controller.Actions)
            {
                CacheDomainAttribute? actionAttr = action.Attributes
                    .OfType<CacheDomainAttribute>()
                    .LastOrDefault()
                    ?? controllerAttr;

                if (actionAttr is not null)
                    action.Filters.Add(new DomainOutputCachePolicy(actionAttr.Domain, actionAttr.ResourceRouteKey));
            }
        }
    }
}