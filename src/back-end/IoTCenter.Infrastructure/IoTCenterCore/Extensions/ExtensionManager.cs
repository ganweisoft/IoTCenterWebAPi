using IoTCenterCore.Environment.Extensions.Features;
using IoTCenterCore.Environment.Extensions.Loaders;
using IoTCenterCore.Environment.Extensions.Manifests;
using IoTCenterCore.Environment.Extensions.Utility;
using IoTCenterCore.Modules;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IoTCenterCore.Environment.Extensions
{
    public class ExtensionManager : IExtensionManager
    {
        private readonly IApplicationContext _applicationContext;

        private readonly IEnumerable<IExtensionDependencyStrategy> _extensionDependencyStrategies;
        private readonly IEnumerable<IExtensionPriorityStrategy> _extensionPriorityStrategies;
        private readonly ITypeFeatureProvider _typeFeatureProvider;
        private readonly IFeaturesProvider _featuresProvider;

        private IDictionary<string, ExtensionEntry> _extensions;
        private IEnumerable<IExtensionInfo> _extensionsInfos;
        private IDictionary<string, FeatureEntry> _features;
        private IFeatureInfo[] _featureInfos;

        private readonly ConcurrentDictionary<string, Lazy<IEnumerable<IFeatureInfo>>> _featureDependencies
            = new ConcurrentDictionary<string, Lazy<IEnumerable<IFeatureInfo>>>();

        private readonly ConcurrentDictionary<string, Lazy<IEnumerable<IFeatureInfo>>> _dependentFeatures
            = new ConcurrentDictionary<string, Lazy<IEnumerable<IFeatureInfo>>>();

        private static readonly Func<IFeatureInfo, IFeatureInfo[], IFeatureInfo[]> GetDependentFeaturesFunc =
            new Func<IFeatureInfo, IFeatureInfo[], IFeatureInfo[]>(
                (currentFeature, fs) => fs
                    .Where(f => f.Dependencies.Any(dep => dep == currentFeature.Id))
                    .ToArray());

        private static readonly Func<IFeatureInfo, IFeatureInfo[], IFeatureInfo[]> GetFeatureDependenciesFunc =
            new Func<IFeatureInfo, IFeatureInfo[], IFeatureInfo[]>(
                (currentFeature, fs) => fs
                    .Where(f => currentFeature.Dependencies.Any(dep => dep == f.Id))
                    .ToArray());

        private bool _isInitialized = false;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        public ExtensionManager(
            IApplicationContext applicationContext,
            IEnumerable<IExtensionDependencyStrategy> extensionDependencyStrategies,
            IEnumerable<IExtensionPriorityStrategy> extensionPriorityStrategies,
            ITypeFeatureProvider typeFeatureProvider,
            IFeaturesProvider featuresProvider,
            ILogger<ExtensionManager> logger)
        {
            _applicationContext = applicationContext;
            _extensionDependencyStrategies = extensionDependencyStrategies;
            _extensionPriorityStrategies = extensionPriorityStrategies;
            _typeFeatureProvider = typeFeatureProvider;
            _featuresProvider = featuresProvider;
            L = logger;
        }

        public ILogger L { get; set; }

        public IExtensionInfo GetExtension(string extensionId)
        {
            EnsureInitialized();

            if (!string.IsNullOrEmpty(extensionId) && _extensions.TryGetValue(extensionId, out ExtensionEntry extension))
            {
                return extension.ExtensionInfo;
            }

            return new NotFoundExtensionInfo(extensionId);
        }

        public IEnumerable<IExtensionInfo> GetExtensions()
        {
            EnsureInitialized();

            return _extensionsInfos;
        }

        public IEnumerable<IFeatureInfo> GetFeatures(string[] featureIdsToLoad)
        {
            EnsureInitialized();

            var allDependencies = featureIdsToLoad
                .SelectMany(featureId => GetFeatureDependencies(featureId))
                .Distinct();

            return _featureInfos
                .Where(f => allDependencies.Any(d => d.Id == f.Id));
        }

        public Task<ExtensionEntry> LoadExtensionAsync(IExtensionInfo extensionInfo)
        {
            EnsureInitialized();

            if (_extensions.TryGetValue(extensionInfo.Id, out ExtensionEntry extension))
            {
                return Task.FromResult(extension);
            }

            return Task.FromResult<ExtensionEntry>(null);
        }

        public async Task<IEnumerable<FeatureEntry>> LoadFeaturesAsync()
        {
            await EnsureInitializedAsync();
            return _features.Values;
        }

        public async Task<IEnumerable<FeatureEntry>> LoadFeaturesAsync(string[] featureIdsToLoad)
        {
            await EnsureInitializedAsync();

            var features = GetFeatures(featureIdsToLoad).Select(f => f.Id).ToList();

            var loadedFeatures = _features.Values
                .Where(f => features.Contains(f.FeatureInfo.Id));

            return loadedFeatures;
        }

        public IEnumerable<IFeatureInfo> GetFeatureDependencies(string featureId)
        {
            EnsureInitialized();

            return _featureDependencies.GetOrAdd(featureId, (key) => new Lazy<IEnumerable<IFeatureInfo>>(() =>
            {
                if (!_features.ContainsKey(key))
                {
                    return Enumerable.Empty<IFeatureInfo>();
                }

                var feature = _features[key].FeatureInfo;

                return GetFeatureDependencies(feature, _featureInfos);
            })).Value;
        }

        public IEnumerable<IFeatureInfo> GetDependentFeatures(string featureId)
        {
            EnsureInitialized();

            return _dependentFeatures.GetOrAdd(featureId, (key) => new Lazy<IEnumerable<IFeatureInfo>>(() =>
            {
                if (!_features.ContainsKey(key))
                {
                    return Enumerable.Empty<IFeatureInfo>();
                }

                var feature = _features[key].FeatureInfo;

                return GetDependentFeatures(feature, _featureInfos);
            })).Value;
        }

        private IEnumerable<IFeatureInfo> GetFeatureDependencies(
            IFeatureInfo feature,
            IFeatureInfo[] features)
        {
            var dependencies = new HashSet<IFeatureInfo>() { feature };
            var stack = new Stack<IFeatureInfo[]>();

            stack.Push(GetFeatureDependenciesFunc(feature, features));

            while (stack.Count > 0)
            {
                var next = stack.Pop();
                foreach (var dependency in next.Where(dependency => !dependencies.Contains(dependency)))
                {
                    dependencies.Add(dependency);
                    stack.Push(GetFeatureDependenciesFunc(dependency, features));
                }
            }

            return _featureInfos.Where(f => dependencies.Any(d => d.Id == f.Id));
        }

        private IEnumerable<IFeatureInfo> GetDependentFeatures(
            IFeatureInfo feature,
            IFeatureInfo[] features)
        {
            var dependencies = new HashSet<IFeatureInfo>() { feature };
            var stack = new Stack<IFeatureInfo[]>();

            stack.Push(GetDependentFeaturesFunc(feature, features));

            while (stack.Count > 0)
            {
                var next = stack.Pop();
                foreach (var dependency in next.Where(dependency => !dependencies.Contains(dependency)))
                {
                    dependencies.Add(dependency);
                    stack.Push(GetDependentFeaturesFunc(dependency, features));
                }
            }

            return _featureInfos.Where(f => dependencies.Any(d => d.Id == f.Id));
        }

        public IEnumerable<IFeatureInfo> GetFeatures()
        {
            EnsureInitialized();

            return _featureInfos;
        }

        private static string GetSourceFeatureNameForType(Type type, string extensionId)
        {
            var attribute = type.GetCustomAttributes<FeatureAttribute>(false).FirstOrDefault();

            return attribute?.FeatureName ?? extensionId;
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            EnsureInitializedAsync().GetAwaiter().GetResult();
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            await _semaphore.WaitAsync();

            try
            {
                if (_isInitialized)
                {
                    return;
                }

                var modules = _applicationContext.Application.Modules;
                var loadedExtensions = new ConcurrentDictionary<string, ExtensionEntry>();

                await modules.ForEachAsync((module) =>
                {
                    if (!module.ModuleInfo.Exists)
                    {
                        return Task.CompletedTask;
                    }
                    var manifestInfo = new ManifestInfo(module.ModuleInfo);

                    var extensionInfo = new ExtensionInfo(module.SubPath, manifestInfo, (mi, ei) =>
                    {
                        return _featuresProvider.GetFeatures(ei, mi);
                    });

                    try
                    {
                        var entry = new ExtensionEntry
                        {
                            ExtensionInfo = extensionInfo,
                            Assembly = module.Assembly,
                            ExportedTypes = module.Assembly.ExportedTypes
                        };

                        loadedExtensions.TryAdd(module.Name, entry);

                        return Task.CompletedTask;
                    }
                    catch (Exception ex)
                    {
                        L.LogError($"���ز����{module.Name}���쳣��{ex.Message}");
                        return Task.CompletedTask;
                    }
                });

                var loadedFeatures = new Dictionary<string, FeatureEntry>();

                var allTypesByExtension = loadedExtensions.SelectMany(extension =>
                    extension.Value.ExportedTypes.Where(IsComponentType)
                    .Select(type => new
                    {
                        ExtensionEntry = extension.Value,
                        Type = type
                    })).ToArray();

                var typesByFeature = allTypesByExtension
                    .GroupBy(typeByExtension => GetSourceFeatureNameForType(
                        typeByExtension.Type,
                        typeByExtension.ExtensionEntry.ExtensionInfo.Id))
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(typesByExtension => typesByExtension.Type).ToArray());

                foreach (var loadedExtension in loadedExtensions)
                {
                    var extension = loadedExtension.Value;

                    foreach (var feature in extension.ExtensionInfo.Features)
                    {
                        if (typesByFeature.TryGetValue(feature.Id, out var featureTypes))
                        {
                            foreach (var type in featureTypes)
                            {
                                _typeFeatureProvider.TryAdd(type, feature);
                            }
                        }
                        else
                        {
                            featureTypes = Array.Empty<Type>();
                        }

                        loadedFeatures.Add(feature.Id, new CompiledFeatureEntry(feature, featureTypes));
                    }
                };

                _featureInfos = Order(loadedFeatures.Values.Select(f => f.FeatureInfo));
                _features = _featureInfos.ToDictionary(f => f.Id, f => loadedFeatures[f.Id]);

                _extensionsInfos = _featureInfos.Where(f => f.Id == f.Extension.Features.First().Id)
                    .Select(f => f.Extension);

                _extensions = _extensionsInfos.ToDictionary(e => e.Id, e => loadedExtensions[e.Id]);

                _isInitialized = true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private bool IsComponentType(Type type)
        {
            return type.IsClass && !type.IsAbstract && type.IsPublic;
        }

        private IFeatureInfo[] Order(IEnumerable<IFeatureInfo> featuresToOrder)
        {
            return featuresToOrder
                .OrderBy(x => x.Id)
                .OrderByDependenciesAndPriorities(HasDependency, GetPriority)
                .ToArray();
        }

        private bool HasDependency(IFeatureInfo f1, IFeatureInfo f2)
        {
            return _extensionDependencyStrategies.Any(s => s.HasDependency(f1, f2));
        }

        private int GetPriority(IFeatureInfo feature)
        {
            return _extensionPriorityStrategies.Sum(s => s.GetPriority(feature));
        }
    }
}
