using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Stelliberty.Tray;

internal static class TrayDependencyDirectoryService
{
    // 解析器必须位于入口程序集，否则 Infrastructure 加载失败时无法自举。
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!Directory.Exists(TrayApplicationLayout.DepsDirectory))
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += ResolveManagedAssembly;
    }

    private static Assembly? ResolveManagedAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var assemblyPath = Path.Combine(TrayApplicationLayout.DepsDirectory, $"{assemblyName.Name}.dll");
        return File.Exists(assemblyPath) ? context.LoadFromAssemblyPath(assemblyPath) : null;
    }
}
