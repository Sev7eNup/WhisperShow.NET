using System.Windows;

namespace Voxwright.Tests.TestHelpers;

public static class WpfTestHelper
{
    private static readonly Lock _lock = new();
    private static bool _initialized;

    public static void EnsureApplication()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            if (Application.Current == null)
                new Application();
            _initialized = true;
        }
    }
}
