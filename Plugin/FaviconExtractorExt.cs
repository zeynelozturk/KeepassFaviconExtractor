using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
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

            PwEntry selectedEntry = host.MainWindow.GetSelectedEntry(false);
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

                await TryAssignCandidatesToEntryAsync(selectedEntry, result.Candidates, sb);
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

        private async System.Threading.Tasks.Task TryAssignCandidatesToEntryAsync(PwEntry selectedEntry, IReadOnlyList<FaviconCandidate> candidates, StringBuilder sb)
        {
            PwDatabase database = host.Database;
            if (database == null || !database.IsOpen)
            {
                sb.AppendLine("Assignment skipped: no open KeePass database.");
                sb.AppendLine();
                return;
            }

            if (candidates == null || candidates.Count == 0)
            {
                sb.AppendLine("Assignment skipped: no ranked candidates to assign.");
                sb.AppendLine();
                return;
            }

            Exception lastError = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                FaviconCandidate candidate = candidates[i];
                if (candidate == null || candidate.IconUri == null)
                {
                    continue;
                }

                try
                {
                    byte[] sourceBytes = await FaviconImageDownloader
                        .DownloadAsync(candidate.IconUri, CancellationToken.None)
                        .ConfigureAwait(true);

                    byte[] normalizedPng = IconNormalizer.NormalizeToPng(
                        sourceBytes,
                        candidate.TypeAttribute,
                        candidate.IconUri.AbsoluteUri);

                    PwUuid assignedUuid = KeePassIconAssigner.AssignNormalizedPngToEntry(
                        database,
                        selectedEntry,
                        normalizedPng);

                    RefreshEntryListIcons(selectedEntry);

                    sb.AppendLine("Assigned custom icon to entry.");
                    sb.AppendLine("Assigned from candidate #" + (i + 1) + ": " + candidate.IconUri);
                    sb.AppendLine("Assigned icon UUID: " + assignedUuid);
                    sb.AppendLine("Normalized PNG size: " + normalizedPng.Length + " bytes");
                    sb.AppendLine();
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    sb.AppendLine("Candidate #" + (i + 1) + " failed: " + ex.Message);
                }
            }

            sb.AppendLine("Assignment failed: all candidates failed.");
            sb.AppendLine();

            if (lastError != null)
            {
                MessageBox.Show(
                    host.MainWindow,
                    "Icon assignment failed for all candidates. Last error: " + lastError.Message,
                    "FaviconExtractor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void RefreshEntryListIcons(PwEntry selectedEntry)
        {
            if (host == null || host.MainWindow == null)
            {
                return;
            }

            try
            {
                var mainWindow = host.MainWindow;
                var activeDocument = mainWindow.DocumentManager != null ? mainWindow.DocumentManager.ActiveDocument : null;
                PwGroup selectedGroup = mainWindow.GetSelectedGroup();
                PwGroup entrySourceGroup = selectedEntry != null ? selectedEntry.ParentGroup : null;
                if (entrySourceGroup == null)
                {
                    entrySourceGroup = selectedGroup;
                }

                if (activeDocument != null)
                {
                    mainWindow.UpdateUI(false, activeDocument, true, selectedGroup, true, entrySourceGroup, true);
                }

                MethodInfo updateImageLists = mainWindow.GetType().GetMethod(
                    "UpdateImageLists",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool) },
                    null);
                if (updateImageLists != null)
                {
                    updateImageLists.Invoke(mainWindow, new object[] { true });
                }

                mainWindow.RefreshEntriesList();

                MethodInfo selectEntry = mainWindow.GetType().GetMethod(
                    "SelectEntry",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(PwEntry), typeof(bool), typeof(bool), typeof(bool), typeof(bool) },
                    null);
                if (selectEntry != null)
                {
                    selectEntry.Invoke(mainWindow, new object[] { selectedEntry, true, false, true, true });
                }
            }
            catch
            {
                host.MainWindow.RefreshEntriesList();
            }
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
