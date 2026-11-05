using System.Reflection;
[assembly: AssemblyTitle(CreditMonitor.Product.Name + " quota bridge")]
[assembly: AssemblyProduct(CreditMonitor.Product.Name)]
[assembly: AssemblyVersion(CreditMonitor.Product.AssemblyVersion)]
[assembly: AssemblyFileVersion(CreditMonitor.Product.AssemblyVersion)]
namespace CreditMonitor
{
    public static class BridgeProgram
    {
        public static int Main() { return ClaudeBridge.Run(); }
    }
}
