using System;
using System.Collections.Concurrent;
using IoTCenterCore.Environment.Extensions.Features;

namespace IoTCenterCore.Environment.Extensions
{
    public class TypeFeatureProvider : ITypeFeatureProvider
    {
        private readonly ConcurrentDictionary<Type, IFeatureInfo> _features
            = new ConcurrentDictionary<Type, IFeatureInfo>();

        public IFeatureInfo GetFeatureForDependency(Type dependency)
        {
            if (_features.TryGetValue(dependency, out IFeatureInfo feature))
            {
                return feature;
            }

            throw new InvalidOperationException($"Could not resolve feature for type {dependency.Name}");
        }

        public void TryAdd(Type type, IFeatureInfo feature)
        {
            _features.TryAdd(type, feature);
        }
    }
}
