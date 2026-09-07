using System;
using System.Text;
using System.Windows.Forms;
using KeePass.Plugins;
using KeePassLib;

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

        private ToolStripMenuItem CreateMenuItem(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += OnMenuItemClick;
            return item;
        }

        private async void OnMenuItemClick(object sender, EventArgs e)
        {
            if (host == null || host.MainWindow == null)
            {
                MessageBox.Show(
                    "Plugin host is not available.",
                    "FaviconExtractor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            PwEntry selectedEntry = host.MainWindow.GetSelectedEntry(true);
            if (selectedEntry == null)
            {
                MessageBox.Show(
                    host.MainWindow,
                    "Select an entry first.",
                    "FaviconExtractor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string url = selectedEntry.Strings.ReadSafe(PwDefs.UrlField);
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(
                    host.MainWindow,
                    "The selected entry does not have a URL.",
                    "FaviconExtractor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            HtmlFaviconDiscoveryResult result;
            try
            {
                result = await FaviconDiscoveryService.DiscoverAsync(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    host.MainWindow,
                    "Favicon discovery failed: " + ex.Message,
                    "FaviconExtractor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Favicon discovery (Level 1 + fallback)");
            sb.AppendLine();
            sb.AppendLine("Input URL: " + url);
            sb.AppendLine("Final page URL: " + result.PageUri);
            sb.AppendLine("Fallback used: " + (result.UsedFallback ? "yes" : "no"));
            if (!string.IsNullOrWhiteSpace(result.Level1Error))
            {
                sb.AppendLine("Level 1 error: " + result.Level1Error);
            }
            if (!string.IsNullOrWhiteSpace(result.DiscoveryNote))
            {
                sb.AppendLine("Note: " + result.DiscoveryNote);
            }
            sb.AppendLine("Candidates found: " + result.Candidates.Count);
            sb.AppendLine();

            if (result.BestCandidate != null)
            {
                sb.AppendLine("Best candidate (preferred sizes: 64 > 32 > 16):");
                sb.AppendLine(FormatCandidate(result.BestCandidate));
                sb.AppendLine();
            }

            if (result.Candidates.Count > 0)
            {
                sb.AppendLine("All ranked candidates:");
                for (int i = 0; i < result.Candidates.Count; ++i)
                {
                    sb.AppendLine((i + 1).ToString() + ". " + FormatCandidate(result.Candidates[i]));
                }
            }
            else
            {
                sb.AppendLine("No usable icon candidates were found in Level 1 or fallback probes.");
            }

            MessageBox.Show(
                host.MainWindow,
                sb.ToString(),
                "FaviconExtractor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static string FormatCandidate(FaviconCandidate candidate)
        {
            string size = candidate.BestSize.HasValue
                ? candidate.BestSize.Value.Width + "x" + candidate.BestSize.Value.Height
                : "unknown";

            string mimeType = string.IsNullOrWhiteSpace(candidate.TypeAttribute)
                ? "(none)"
                : candidate.TypeAttribute;

            return "score=" + candidate.Score
                + ", source='" + (string.IsNullOrWhiteSpace(candidate.Source) ? "unknown" : candidate.Source) + "'"
                + ", rel='" + candidate.RelAttribute + "'"
                + ", type='" + mimeType + "'"
                + ", size=" + size
                + ", url=" + candidate.IconUri;
        }
    }
}
