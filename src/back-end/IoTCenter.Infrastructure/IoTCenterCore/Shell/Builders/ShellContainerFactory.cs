using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using IoTCenterCore.Environment.Extensions;
using IoTCenterCore.Environment.Extensions.Features;
using IoTCenterCore.Environment.Shell.Builders.Models;
using IoTCenterCore.Modules;

namespace IoTCenterCore.Environment.Shell.Builders
{
    public class ShellContainerFactory : IShellContainerFactory
    {
        private IFeatureInfo _applicationFeature;

        private readonly IHostEnvironment _hostingEnvironment;
        private readonly IExtensionManager _extensionManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly IServiceCollection _applicationServices;

        public ShellContainerFactory(
            IHostEnvironment hostingEnvironment,
            IExtensionManager extensionManager,
            IServiceProvider serviceProvider,
            IServiceCollection applicationServices)
        {
            _hostingEnvironment = hostingEnvironment;
            _extensionManager = extensionManager;
            _applicationServices = applicationServices;
            _serviceProvider = serviceProvider;
        }

        public void AddCoreServices(IServiceCollection services)
        {
            services.TryAddScoped<IShellStateUpdater, ShellStateUpdater>();
            services.TryAddScoped<IShellStateManager, NullShellStateManager>();
            services.AddScoped<IShellDescriptorManagerEventHandler, ShellStateCoordinator>();
        }

        public IServiceProvider CreateContainer(ShellSettings settings, ShellBlueprint blueprint)
        {
            var tenantServiceCollection = _serviceProvider.CreateChildContainer(_applicationServices);

            tenantServiceCollection.AddSingleton(settings);
            tenantServiceCollection.AddSingleton(sp =>
            {
                var shellSettings = sp.GetRequiredService<ShellSettings>();
                return shellSettings.ShellConfiguration;
            });

            tenantServiceCollection.AddSingleton(blueprint.Descriptor);
            tenantServiceCollection.AddSingleton(blueprint);

            AddCoreServices(tenantServiceCollection);


            var moduleServiceCollection = _serviceProvider.CreateChildContainer(_applicationServices);

            foreach (var dependency in blueprint.Dependencies.Where(t => typeof(IStartup).IsAssignableFrom(t.Key)))
            {
                moduleServiceCollection.AddSingleton(typeof(IStartup), dependency.Key);
                tenantServiceCollection.AddSingleton(typeof(IStartup), dependency.Key);
            }

            EnsureApplicationFeature();

            foreach (var rawStartup in blueprint.Dependencies.Keys.Where(t => t.Name == "Startup"))
            {
                if (typeof(IStartup).IsAssignableFrom(rawStartup))
                {
                    continue;
                }

                if (blueprint.Dependencies.TryGetValue(rawStartup, out var startupFeature) && startupFeature.FeatureInfo.Id == _applicationFeature.Id)
                {
                    continue;
                }

                var configureServicesMethod = rawStartup.GetMethod(
                    nameof(IStartup.ConfigureServices),
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    CallingConventions.Any,
                    new Type[] { typeof(IServiceCollection) },
                    null);

                var configureMethod = rawStartup.GetMethod(
                    nameof(IStartup.Configure),
                    BindingFlags.Public | BindingFlags.Instance);

                var orderProperty = rawStartup.GetProperty(
                    nameof(IStartup.Order),
                    BindingFlags.Public | BindingFlags.Instance);

                var configureOrderProperty = rawStartup.GetProperty(
                    nameof(IStartup.ConfigureOrder),
                    BindingFlags.Public | BindingFlags.Instance);

                moduleServiceCollection.AddSingleton(rawStartup);
                tenantServiceCollection.AddSingleton(rawStartup);

                moduleServiceCollection.AddSingleton<IStartup>(sp =>
                {
                    var startupInstance = sp.GetService(rawStartup);
                    return new StartupBaseMock(startupInstance, configureServicesMethod, configureMethod, orderProperty, configureOrderProperty);
                });

                tenantServiceCollection.AddSingleton<IStartup>(sp =>
                {
                    var startupInstance = sp.GetService(rawStartup);
                    return new StartupBaseMock(startupInstance, configureServicesMethod, configureMethod, orderProperty, configureOrderProperty);
                });
            }

            moduleServiceCollection.AddSingleton(settings);
            moduleServiceCollection.AddSingleton(sp =>
            {
                var shellSettings = sp.GetRequiredService<ShellSettings>();
                return shellSettings.ShellConfiguration;
            });

            var moduleServiceProvider = moduleServiceCollection.BuildServiceProvider(true);

            var featureAwareServiceCollection = new FeatureAwareServiceCollection(tenantServiceCollection);

            var startups = moduleServiceProvider.GetServices<IStartup>();

            startups = startups.OrderBy(s => s.Order);

            foreach (var startup in startups)
            {
                var feature = blueprint.Dependencies.FirstOrDefault(x => x.Key == startup.GetType()).Value?.FeatureInfo;


                featureAwareServiceCollection.SetCurrentFeature(feature ?? _applicationFeature);
                startup.ConfigureServices(featureAwareServiceCollection);
            }

            (moduleServiceProvider as IDisposable).Dispose();

            var shellServiceProvider = tenantServiceCollection.BuildServiceProvider(true);

            var typeFeatureProvider = shellServiceProvider.GetRequiredService<ITypeFeatureProvider>();

            foreach (var featureServiceCollection in featureAwareServiceCollection.FeatureCollections)
            {
                foreach (var serviceDescriptor in featureServiceCollection.Value)
                {
                    var type = serviceDescriptor.GetImplementationType();

                    if (type != null)
                    {
                        var feature = featureServiceCollection.Key;

                        if (feature == _applicationFeature)
                        {
                            var attribute = type.GetCustomAttributes<FeatureAttribute>(false).FirstOrDefault();

                            if (attribute != null)
                            {
                                feature = featureServiceCollection.Key.Extension.Features
                                    .FirstOrDefault(f => f.Id == attribute.FeatureName)
                                    ?? feature;
                            }
                        }

                        typeFeatureProvider.TryAdd(type, feature);
                    }
                }
            }

            return shellServiceProvider;
        }

        private void EnsureApplicationFeature()
        {
            if (_applicationFeature == null)
            {
                lock (this)
                {
                    if (_applicationFeature == null)
                    {
                        _applicationFeature = _extensionManager.GetFeatures()
                            .FirstOrDefault(f => f.Id == _hostingEnvironment.ApplicationName);
                    }
                }
            }
        }
    }
}
