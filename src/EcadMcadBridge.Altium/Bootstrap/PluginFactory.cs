using System.Runtime.InteropServices;
using DXP;
using EcadMcadBridge.Altium.Application;

[assembly: ComVisible(true)]
[assembly: Guid("f2c9b1a4-6d5e-4c8b-9a31-7e0f2b8d4c10")]

namespace CSharpPlugin
{
    /// <summary>The contract Altium uses to create an extension (D-017). Names are fixed by the host.</summary>
    public interface IPluginFactory
    {
        object InvokePluginFactory(IClient client);
    }

    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class PluginFactory : IPluginFactory
    {
        public object InvokePluginFactory(IClient client)
        {
            BridgeLogger.Write("INFO", "BOOTSTRAP_FACTORY", "addin=0.1.0 clr=" + Environment.Version);
            return new BridgeServerModule(client, BridgeServerModule.ServerName);
        }
    }
}
