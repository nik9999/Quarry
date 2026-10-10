using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quarry.Generation;

namespace Quarry.Operations.Budgeting;

/// <summary>
/// DI wiring for the budget governor. Registers the
/// <see cref="TenantSpendLedger"/> and decorates the already-registered
/// <see cref="IRagPipeline"/> with the <see cref="BudgetEnforcingPipeline"/> so
/// enforcement is transparent to call sites.
/// </summary>
public static class BudgetEnforcementRegistration
{
    /// <summary>
    /// Add the budget governor in front of the registered
    /// <see cref="IRagPipeline"/>.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="policy">The budget policy. Defaults to <see cref="BudgetPolicy.Default"/>.</param>
    /// <param name="window">
    /// The ledger's rolling window. Defaults to one hour.
    /// </param>
    /// <param name="tenantAccessor">
    /// Resolves the current tenant id. Defaults to a single <c>"default"</c> tenant.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBudgetEnforcement(
        this IServiceCollection services,
        BudgetPolicy? policy = null,
        TimeSpan? window = null,
        Func<string>? tenantAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var effectivePolicy = policy ?? BudgetPolicy.Default;
        var effectiveWindow = window ?? TimeSpan.FromHours(1);

        services.TryAddSingleton(sp =>
            new TenantSpendLedger(effectiveWindow, sp.GetService<TimeProvider>()));

        var innerDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IRagPipeline))
            ?? throw new InvalidOperationException(
                "AddBudgetEnforcement requires an IRagPipeline to already be registered.");
        services.Remove(innerDescriptor);

        services.Add(ServiceDescriptor.Describe(
            typeof(IRagPipeline),
            sp => new BudgetEnforcingPipeline(
                (IRagPipeline)CreateInner(innerDescriptor, sp),
                effectivePolicy,
                sp.GetRequiredService<TenantSpendLedger>(),
                tenantAccessor,
                timeProvider: sp.GetService<TimeProvider>()),
            innerDescriptor.Lifetime));

        return services;
    }

    private static object CreateInner(ServiceDescriptor descriptor, IServiceProvider sp)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(sp);
        }

        return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}
