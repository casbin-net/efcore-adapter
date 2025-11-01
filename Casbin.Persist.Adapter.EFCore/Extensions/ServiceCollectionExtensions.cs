using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Casbin.Persist.Adapter.EFCore.Extensions
{
    /// <summary>
    /// Extension methods for registering EFCoreAdapter with dependency injection.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the EFCoreAdapter to the service collection.
        /// The adapter will resolve the DbContext from the service provider on each operation,
        /// preventing issues with disposed contexts when used with long-lived services.
        /// </summary>
        /// <typeparam name="TKey">The type of the primary key for the policy entities.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime for the adapter. Default is Scoped.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEFCoreAdapter<TKey>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped) where TKey : IEquatable<TKey>
        {
            var descriptor = new ServiceDescriptor(
                typeof(IAdapter),
                sp => new EFCoreAdapter<TKey>(sp),
                lifetime);
            
            services.TryAdd(descriptor);
            return services;
        }

        /// <summary>
        /// Adds the EFCoreAdapter with custom policy type to the service collection.
        /// The adapter will resolve the DbContext from the service provider on each operation,
        /// preventing issues with disposed contexts when used with long-lived services.
        /// </summary>
        /// <typeparam name="TKey">The type of the primary key for the policy entities.</typeparam>
        /// <typeparam name="TPersistPolicy">The type of the persist policy entity.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime for the adapter. Default is Scoped.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEFCoreAdapter<TKey, TPersistPolicy>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TKey : IEquatable<TKey>
            where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        {
            var descriptor = new ServiceDescriptor(
                typeof(IAdapter),
                sp => new EFCoreAdapter<TKey, TPersistPolicy>(sp),
                lifetime);
            
            services.TryAdd(descriptor);
            return services;
        }
    }
}
