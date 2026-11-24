using System.Windows.Forms;
using DXP;

namespace CSharpPlugin
{
    /// <summary>A tiny panel whose only purpose is to make AD start the server at load time.</summary>
    internal sealed class BridgePanelForm : ServerPanelForm
    {
        public const string PanelName = "EcadMcadBridgePanel";
        public const string Caption = "ECAD-MCAD Bridge";

        public BridgePanelForm()
        {
            Text = Caption;
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Use Tools > Export Smoke Snapshot to write a diagnostic PCB snapshot.",
            });
        }
    }

    internal sealed class BridgePanelView : ServerPanelView
    {
        public BridgePanelView(BridgePanelForm form)
            : base(form, BridgePanelForm.PanelName, BridgePanelForm.Caption)
        {
        }
    }

    public partial class BridgeServerModule
    {
        private BridgePanelView? _panel;

        partial void RegisterViews(IClient client)
        {
            _panel = new BridgePanelView(new BridgePanelForm());
            AddServerView(_panel);
            client.AddServerView(_panel);
        }
    }
}
