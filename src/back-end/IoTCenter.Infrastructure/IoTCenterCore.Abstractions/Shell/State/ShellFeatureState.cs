using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IoTCenterCore.Environment.Shell.State
{
    public class ShellFeatureState
    {
        public string Id { get; set; }
        public State InstallState { get; set; }
        public State EnableState { get; set; }

        [JsonIgnore]
        public bool IsInstalled { get { return InstallState == State.Up; } }

        [JsonIgnore]
        public bool IsEnabled { get { return EnableState == State.Up; } }

        [JsonIgnore]
        public bool IsDisabled { get { return EnableState == State.Down || EnableState == State.Undefined; } }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum State
        {
            Undefined,
            Rising,
            Up,
            Falling,
            Down,
        }
    }
}
