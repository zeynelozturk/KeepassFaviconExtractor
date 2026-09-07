using System;
using System.Windows.Forms;
using KeePass.Plugins;

namespace FaviconExtractor
{
    public sealed class FaviconExtractorExt : Plugin
    {
        private IPluginHost host;

        public override bool Initialize(IPluginHost pluginHost)
        {
            if (pluginHost == null)
            {
                return false;
            }

            host = pluginHost;
            return true;
        }

        public override ToolStripMenuItem GetMenuItem(PluginMenuType type)
        {
            if (type == PluginMenuType.Main)
            {
                return CreateMenuItem("Favicon Extractor...");
            }

            if (type == PluginMenuType.Entry)
            {
                return CreateMenuItem("Fetch Favicon");
            }

            return null;
        }

        public override void Terminate()
        {
            host = null;
        }

        private static ToolStripMenuItem CreateMenuItem(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += OnMenuItemClick;
            return item;
        }

        private static void OnMenuItemClick(object sender, EventArgs e)
        {
            MessageBox.Show(
                "FaviconExtractor loaded successfully. Favicon fetching will be added next.",
                "FaviconExtractor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
