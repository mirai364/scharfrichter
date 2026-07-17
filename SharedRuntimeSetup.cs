using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

internal static class SharedRuntimeSetup
{
    [SuppressMessage("Usage", "CA2255", Justification = "Register legacy code pages once for migrated .NET applications.")]
    [ModuleInitializer]
    internal static void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}