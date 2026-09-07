using System;
using System.Windows.Forms;
using KeePass.Plugins;

namespace KeePassFaviconExtractor
{
    public class KeePassFaviconExtractorExt : Plugin
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
                "KeePassFaviconExtractor loaded successfully. Favicon fetching will be added next.",
                "KeePassFaviconExtractor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

namespace KeepassFaviconExtractor
{
    public sealed class KeepassFaviconExtractorExt : KeePassFaviconExtractor.KeePassFaviconExtractorExt
    {
    }
}
}
