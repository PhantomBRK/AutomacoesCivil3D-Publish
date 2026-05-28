using System;
using Xbim.Common;
using Xbim.Common.Configuration;

namespace AutomacoesCivil3D
{
    internal static class XbimServiceBootstrap
    {
        private static readonly object SyncRoot = new object();
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (SyncRoot)
            {
                if (_initialized)
                    return;

                try
                {
                    XbimServices.Current.ConfigureServices(
                        services => services.AddXbimToolkit(options => options.AddMemoryModel())
                    );
                }
                catch (InvalidOperationException ex)
                    when (ex.Message.IndexOf("already been built", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Another command in the same AutoCAD session already initialized xBIM.
                }

                _initialized = true;
            }
        }
    }
}
