using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using IoTCenterCore.Environment.Extensions;
using IoTCenterCore.Environment.Extensions.Features;
using IoTCenterCore.Environment.Shell.Descriptor;
using IoTCenterCore.Environment.Shell.Descriptor.Models;

namespace IoTCenterCore.Environment.Shell
{
    public class ShellDescriptorFeaturesManager : IShellDescriptorFeaturesManager
    {
        private readonly IExtensionManager _extensionManager;
        private readonly IEnumerable<ShellFeature> _alwaysEnabledFeatures;
        private readonly IShellDescriptorManager _shellDescriptorManager;
        private readonly ILogger _logger;

        public FeatureDependencyNotificationHandler FeatureDependencyNotification { get; set; }

        public ShellDescriptorFeaturesManager(
            IExtensionManager extensionManager,
            IEnumerable<ShellFeature> shellFeatures,
            IShellDescriptorManager shellDescriptorManager,
            ILogger<ShellFeaturesManager> logger)
        {
            _extensionManager = extensionManager;
            _alwaysEnabledFeatures = shellFeatures.Where(f => f.AlwaysEnabled).ToArray();
            _shellDescriptorManager = shellDescriptorManager;
            _logger = logger;
        }

        public async Task<(IEnumerable<IFeatureInfo>, IEnumerable<IFeatureInfo>)> UpdateFeaturesAsync(ShellDescriptor shellDescriptor,
            IEnumerable<IFeatureInfo> featuresToDisable, IEnumerable<IFeatureInfo> featuresToEnable, bool force)
        {
            var alwaysEnabledIds = _alwaysEnabledFeatures.Select(sf => sf.Id).ToArray();

            var enabledFeatures = _extensionManager.GetFeatures().Where(f =>
                shellDescriptor.Features.Any(sf => sf.Id == f.Id)).ToList();

            var enabledFeatureIds = enabledFeatures.Select(f => f.Id).ToArray();

            var AllFeaturesToDisable = featuresToDisable
                .Where(f => !alwaysEnabledIds.Contains(f.Id))
                .SelectMany(feature => GetFeaturesToDisable(feature, enabledFeatureIds, force))
                .Distinct()
                .ToList();

            if (AllFeaturesToDisable.Count > 0)
            {
                foreach (var feature in AllFeaturesToDisable)
                {
                    enabledFeatures.Remove(feature);

                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Feature '{FeatureName}' was disabled", feature.Id);
                    }
                }
            }

            enabledFeatureIds = enabledFeatures.Select(f => f.Id).ToArray();

            var AllFeaturesToEnable = featuresToEnable
                .SelectMany(feature => GetFeaturesToEnable(feature, enabledFeatureIds, force))
                .Distinct()
                .ToList();

            if (AllFeaturesToEnable.Count > 0)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    foreach (var feature in AllFeaturesToEnable)
                    {
                        _logger.LogInformation("Enabling feature '{FeatureName}'", feature.Id);
                    }
                }

                enabledFeatures = enabledFeatures.Concat(AllFeaturesToEnable).Distinct().ToList();
            }

            if (AllFeaturesToDisable.Count > 0 || AllFeaturesToEnable.Count > 0)
            {
                await _shellDescriptorManager.UpdateShellDescriptorAsync(
                    shellDescriptor.SerialNumber,
                    enabledFeatures.Select(x => new ShellFeature(x.Id)).ToList(),
                    shellDescriptor.Parameters);
            }

            return (AllFeaturesToDisable, AllFeaturesToEnable);
        }

        private IEnumerable<IFeatureInfo> GetFeaturesToEnable(IFeatureInfo featureInfo, IEnumerable<string> enabledFeatureIds, bool force)
        {
            var featuresToEnable = _extensionManager
                .GetFeatureDependencies(featureInfo.Id)
                .Where(f => !enabledFeatureIds.Contains(f.Id))
                .ToList();

            if (featuresToEnable.Count > 1 && !force)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(" To enable '{FeatureId}', additional features need to be enabled.", featureInfo.Id);
                }

                FeatureDependencyNotification?.Invoke("If {0} is enabled, then you'll also need to enable {1}.", featureInfo, featuresToEnable.Where(f => f.Id != featureInfo.Id));

                return Enumerable.Empty<IFeatureInfo>();
            }

            return featuresToEnable;
        }

        private IEnumerable<IFeatureInfo> GetFeaturesToDisable(IFeatureInfo featureInfo, IEnumerable<string> enabledFeatureIds, bool force)
        {
            var featuresToDisable = _extensionManager
                .GetDependentFeatures(featureInfo.Id)
                .Where(f => enabledFeatureIds.Contains(f.Id))
                .ToList();

            if (featuresToDisable.Count > 1 && !force)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(" To disable '{FeatureId}', additional features need to be disabled.", featureInfo.Id);
                }

                FeatureDependencyNotification?.Invoke("If {0} is disabled, then you'll also need to disable {1}.", featureInfo, featuresToDisable.Where(f => f.Id != featureInfo.Id));

                return Enumerable.Empty<IFeatureInfo>();
            }

            return featuresToDisable;
        }
    }
}
