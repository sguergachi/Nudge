// WaylandDotnet stubs to satisfy missing references during build.
// Minimal implementations that compile but do not provide real functionality.
// Namespace: WaylandDotnet
namespace WaylandDotnet
{
    // Stub for WlDisplay
    public sealed class WlDisplay : System.IDisposable
    {
        public static WlDisplay Connect() => new WlDisplay();
        public System.IntPtr Handle => System.IntPtr.Zero;
        public Registry GetRegistry() => new Registry();
        public void Roundtrip() { /* no-op */ }
        public void Dispatch() { /* no-op */ }
        public void Dispose() { /* no-op */ }
    }

    // Stub for Registry used to bind Wayland interfaces
    public sealed class Registry
    {
        // Event signature matches usage: (name, interfaceName, version)
        public event System.Action<uint, string, int>? OnGlobal;
        // Simulate binding by returning a new instance of the requested type.
        public T Bind<T>(uint name, int version) where T : new() => new T();
        // Helper to raise OnGlobal (not used in stubs)
        internal void Raise(uint name, string iface, int version) => OnGlobal?.Invoke(name, iface, version);
    }

    // Stub for WlSeat
    public sealed class WlSeat
    {
        public const string InterfaceName = "wl_seat";
        public System.IntPtr Handle => System.IntPtr.Zero;
    }

    // Stub for ExtIdleNotifierV1 and related types
    public sealed class ExtIdleNotifierV1
    {
        public const string InterfaceName = "ext_idle_notifier_v1";
        public ExtIdleNotificationV1 GetIdleNotification(int timeoutMs, WlSeat seat) => new ExtIdleNotificationV1();
        public void Destroy() { }
    }

    public sealed class ExtIdleNotificationV1
    {
        public event System.Action? OnIdled;
        public event System.Action? OnResumed;
        public void Destroy() { }
        // Methods to simulate idle/resume (not used directly)
        internal void SimulateIdle() => OnIdled?.Invoke();
        internal void SimulateResume() => OnResumed?.Invoke();
    }
}
// Namespace for staging sub-namespace used in imports
namespace WaylandDotnet.Staging
{
    // Placeholder type to ensure the namespace exists in compiled output
    public sealed class StagingPlaceholder { }
}
