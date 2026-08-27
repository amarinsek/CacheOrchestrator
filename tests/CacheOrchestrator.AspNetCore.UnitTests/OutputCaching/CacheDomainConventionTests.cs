using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System.Reflection;

namespace CacheOrchestrator.AspNetCore.UnitTests.OutputCaching;

public class CacheDomainConventionTests
{
    private readonly CacheDomainConvention _sut = new();

    // =========================
    // No attribute
    // =========================

    [Fact]
    public void Apply_WhenNoAttribute_DoesNotAddFilter()
    {
        ApplicationModel application = CreateApplicationModel(typeof(ControllerWithoutAttribute));

        _sut.Apply(application);

        ActionModel action = application.Controllers[0].Actions[0];
        action.Filters.OfType<DomainOutputCachePolicy>().Should().BeEmpty();
    }

    // =========================
    // Controller-level attribute
    // =========================

    [Fact]
    public void Apply_WhenControllerHasAttribute_AddsPolicyToAllActions()
    {
        ApplicationModel application = CreateApplicationModel(typeof(ControllerWithAttribute));

        _sut.Apply(application);

        ControllerModel controller = application.Controllers[0];
        controller.Actions.Should().HaveCount(2);

        foreach (ActionModel action in controller.Actions)
        {
            DomainOutputCachePolicy? policy = action.Filters.OfType<DomainOutputCachePolicy>().SingleOrDefault();
            policy.Should().NotBeNull();
            policy.FixedDomain.Should().Be("products");
        }
    }

    // =========================
    // Action-level attribute
    // =========================

    [Fact]
    public void Apply_WhenActionHasAttribute_AddsPolicyToThatAction()
    {
        ApplicationModel application = CreateApplicationModel(typeof(ControllerWithActionAttribute));

        _sut.Apply(application);

        ControllerModel controller = application.Controllers[0];

        ActionModel actionWithAttr = controller.Actions.Single(a => a.ActionName == nameof(ControllerWithActionAttribute.GetCached));
        actionWithAttr.Filters.OfType<DomainOutputCachePolicy>().Should().HaveCount(1);

        ActionModel actionWithoutAttr = controller.Actions.Single(a => a.ActionName == nameof(ControllerWithActionAttribute.GetNotCached));
        actionWithoutAttr.Filters.OfType<DomainOutputCachePolicy>().Should().BeEmpty();
    }

    // =========================
    // Action overrides controller
    // =========================

    [Fact]
    public void Apply_WhenBothControllerAndActionHaveAttribute_ActionWins()
    {
        ApplicationModel application = CreateApplicationModel(typeof(ControllerWithBothAttributes));

        _sut.Apply(application);

        ActionModel action = application.Controllers[0].Actions
            .Single(a => a.ActionName == nameof(ControllerWithBothAttributes.GetDetail));

        DomainOutputCachePolicy policy = action.Filters.OfType<DomainOutputCachePolicy>().Should().ContainSingle().Subject;
        policy.FixedDomain.Should().Be("product-detail");
    }

    // =========================
    // Helpers + test controllers
    // =========================

    private static ApplicationModel CreateApplicationModel(Type controllerType)
    {
        var application = new ApplicationModel();
        var controllerModel = new ControllerModel(controllerType.GetTypeInfo(), [.. controllerType.GetCustomAttributes(inherit: true).Cast<object>()])
        {
            ControllerName = controllerType.Name.Replace("Controller", "")
        };

        foreach (MethodInfo method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            var actionModel = new ActionModel(method, [.. method.GetCustomAttributes(inherit: true).Cast<object>()])
            {
                ActionName = method.Name,
                Controller = controllerModel
            };
            controllerModel.Actions.Add(actionModel);
        }

        application.Controllers.Add(controllerModel);
        return application;
    }

    private sealed class ControllerWithoutAttribute
    {
        public void Index() { }
    }

    [CacheDomain("products")]
    private sealed class ControllerWithAttribute
    {
        public void Index() { }
        public void Details() { }
    }

    private sealed class ControllerWithActionAttribute
    {
        [CacheDomain("products")]
        public void GetCached() { }

        public void GetNotCached() { }
    }

    [CacheDomain("products")]
    private sealed class ControllerWithBothAttributes
    {
        [CacheDomain("product-detail")]
        public void GetDetail() { }
    }
}
