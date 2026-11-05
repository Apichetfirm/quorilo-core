using System;
using System.Windows;
namespace CreditMonitor
{
    internal static class CompiledViews
    {
        internal static object Load(string name)
        {
            return Application.LoadComponent(new Uri("/"+typeof(CompiledViews).Assembly.GetName().Name+";component/"+name.ToLowerInvariant(),UriKind.Relative));
        }
    }
}
