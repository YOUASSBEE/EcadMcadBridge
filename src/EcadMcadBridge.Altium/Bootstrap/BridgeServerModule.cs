using System.Runtime.InteropServices;
using DXP;
using EcadMcadBridge.Altium.Application;

namespace CSharpPlugin
{
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public partial class BridgeServerModule : ServerModule
    {
        public const string ServerName = "EcadMcadBridge";

        public BridgeServerModule(IClient client, string moduleName)
            : base(client, moduleName)
        {
            BridgeLogger.Write("INFO", "BOOTSTRAP_MODULE", "module=" + moduleName);
            RegisterViews(client);
        }

        partial void RegisterViews(IClient client);

        protected override void InitializeCommands()
        {
            var launcher = (CommandLauncher)CommandLauncher;
            launcher.RegisterCommand(
                ExportSmokeSnapshotHandler.CommandName,
                ExportSmokeSnapshotHandler.Execute,
                ExportSmokeSnapshotHandler.GetState);
            BridgeLogger.Write("INFO", "BOOTSTRAP_COMMANDS", "command=" + ExportSmokeSnapshotHandler.CommandName);
        }

        protected override IServerDocument NewDocumentInstance(string kind, string fileName) => null!;
    }
}
