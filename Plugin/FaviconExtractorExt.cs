using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private bool isExtractRunning;
        private CancellationTokenSource extractCancellationTokenSource;

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
                ToolStripMenuItem root = new ToolStripMenuItem("Favicon Extractor");
                root.DropDownItems.Add(CreateMenuItem("Extract favicon", OnExtractFaviconClick));
                root.DropDownItems.Add(CreateMenuItem("Diagnostics", OnDiagnosticsMenuItemClick));
                return root;
            }

            if (type == PluginMenuType.Entry)
            {
                return CreateMenuItem("Extract favicon", OnExtractFaviconClick);
            }

            return null;
        }

        public override void Terminate()
        {
            host = null;
        }

        private ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += onClick;
            return item;
        }

        private async void OnExtractFaviconClick(object sender, EventArgs e)
        {
            if (host == null || host.MainWindow == null)
            {
                return;
            }

            if (isExtractRunning)
            {
                return;
            }

            isExtractRunning = true;
            extractCancellationTokenSource = new CancellationTokenSource();

            ExtractionStatusForm statusForm = new ExtractionStatusForm();
            statusForm.PositionNearOwner(host.MainWindow as Form);
            statusForm.AttachCancelAction(() =>
            {
                CancellationTokenSource cts = extractCancellationTokenSource;
                if (cts != null && !cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            });
            statusForm.Show(host.MainWindow);

            try
            {
                PwEntry selectedEntry = host.MainWindow.GetSelectedEntry(false);
                if (selectedEntry == null)
                {
                    statusForm.AppendLineSafe("Extraction failed.");
                    statusForm.AppendLineSafe("Reason: Select an entry first.");
                    statusForm.MarkFailed();
                    return;
                }

                string url = selectedEntry.Strings.ReadSafe(PwDefs.UrlField);
                if (string.IsNullOrWhiteSpace(url))
                {
                    statusForm.AppendLineSafe("Extraction failed.");
                    statusForm.AppendLineSafe("Reason: The selected entry does not have a URL.");
                    statusForm.MarkFailed();
                    return;
                }

                CancellationToken cancellationToken = extractCancellationTokenSource.Token;

                statusForm.AppendLineSafe("Searching icon candidates...");
                HtmlFaviconDiscoveryResult result = await FaviconDiscoveryService
                    .DiscoverAsync(url, cancellationToken)
                    .ConfigureAwait(true);

                cancellationToken.ThrowIfCancellationRequested();

                if (result == null || result.Candidates == null || result.Candidates.Count == 0)
                {
                    statusForm.AppendLineSafe("Extraction failed.");
                    if (result != null && !string.IsNullOrWhiteSpace(result.DiscoveryNote))
                    {
                        statusForm.AppendLineSafe("Reason: " + result.DiscoveryNote);
                    }
                    else
                    {
                        statusForm.AppendLineSafe("Reason: No usable icon candidates were found.");
                    }

                    statusForm.MarkFailed();
                    return;
                }

                statusForm.AppendLineSafe("Downloading and assigning icon...");
                AssignmentAttemptResult assignmentResult = await TryAssignCandidatesToEntryAsync(
                    selectedEntry,
                    result.Candidates,
                    result.PageUri,
                    cancellationToken,
                    new StringBuilder()).ConfigureAwait(true);

                cancellationToken.ThrowIfCancellationRequested();

                if (assignmentResult.Success)
                {
                    statusForm.SetAssignedIconPreview(assignmentResult.AssignedIconPngBytes);
                    statusForm.AppendLineSafe(string.Empty);
                    statusForm.AppendLineSafe("Success: icon assigned.");
                    statusForm.MarkCompletedWithCountdown(2);
                    return;
                }

                statusForm.AppendLineSafe("Extraction failed.");
                if (!string.IsNullOrWhiteSpace(assignmentResult.FailureReason))
                {
                    statusForm.AppendLineSafe("Reason: " + assignmentResult.FailureReason);
                }

                statusForm.MarkFailed();
            }
            catch (OperationCanceledException)
            {
                statusForm.AppendLineSafe("Canceled. Existing icon was left unchanged.");
                statusForm.MarkCanceled();
            }
            catch (Exception ex)
            {
                statusForm.AppendLineSafe("Extraction failed.");
                statusForm.AppendLineSafe("Reason: " + ex.Message);
                statusForm.MarkFailed();
            }
            finally
            {
                if (extractCancellationTokenSource != null)
                {
                    extractCancellationTokenSource.Dispose();
                    extractCancellationTokenSource = null;
                }

                isExtractRunning = false;
            }
        }

        private async void OnDiagnosticsMenuItemClick(object sender, EventArgs e)
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

            DiagnosticsProgressForm diagnosticsForm = new DiagnosticsProgressForm();
            diagnosticsForm.PositionNearOwner(host.MainWindow as Form);
            diagnosticsForm.Show(host.MainWindow);

            try
            {
                await FallbackDiagnosticsService
                    .RunAsync(CancellationToken.None, diagnosticsForm.AppendLineSafe)
                    .ConfigureAwait(true);

                diagnosticsForm.MarkCompleted();
            }
            catch (Exception ex)
            {
                diagnosticsForm.MarkFailed(ex.Message);
            }
        }

        private async System.Threading.Tasks.Task<AssignmentAttemptResult> TryAssignCandidatesToEntryAsync(PwEntry selectedEntry, IReadOnlyList<FaviconCandidate> candidates, Uri pageUri, CancellationToken cancellationToken, StringBuilder sb)
        {
            PwDatabase database = host.Database;
            if (database == null || !database.IsOpen)
            {
                return AssignmentAttemptResult.Fail("No open KeePass database.");
            }

            if (candidates == null || candidates.Count == 0)
            {
                return AssignmentAttemptResult.Fail("No ranked icon candidates to assign.");
            }

            HashSet<string> attemptedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool attemptedHtmlCandidate = false;
            Exception lastError = null;
            bool assigned = false;
            FaviconCandidate assignedCandidate = null;
            PwUuid assignedUuid = null;
            int assignedNormalizedPngSize = 0;
            byte[] assignedNormalizedPngBytes = null;
            int assignedCandidateIndex = -1;
            int highestFailedScoreBeforeAssignment = int.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                FaviconCandidate candidate = candidates[i];
                if (candidate == null || candidate.IconUri == null)
                {
                    continue;
                }

                string candidateUrl = candidate.IconUri.AbsoluteUri;
                if (!attemptedUrls.Add(candidateUrl))
                {
                    continue;
                }

                if (IsHtmlCandidate(candidate))
                {
                    attemptedHtmlCandidate = true;
                }

                if (IsUnsupportedForAssignment(candidate))
                {
                    sb.AppendLine("Candidate #" + (i + 1) + " skipped: unsupported image format for .NET Framework decoder.");
                    continue;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] sourceBytes = await FaviconImageDownloader
                        .DownloadAsync(candidate.IconUri, cancellationToken)
                        .ConfigureAwait(true);

                    byte[] normalizedPng = IconNormalizer.NormalizeToPng(
                        sourceBytes,
                        candidate.TypeAttribute,
                        candidate.IconUri.AbsoluteUri);

                    cancellationToken.ThrowIfCancellationRequested();

                    PwUuid currentAssignedUuid = KeePassIconAssigner.AssignNormalizedPngToEntry(
                        database,
                        selectedEntry,
                        normalizedPng);

                    RefreshEntryListIcons(selectedEntry);

                    assigned = true;
                    assignedCandidate = candidate;
                    assignedCandidateIndex = i + 1;
                    assignedNormalizedPngSize = normalizedPng.Length;
                    assignedNormalizedPngBytes = normalizedPng;
                    assignedUuid = currentAssignedUuid;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    highestFailedScoreBeforeAssignment = Math.Max(highestFailedScoreBeforeAssignment, candidate.Score);
                    sb.AppendLine("Candidate #" + (i + 1) + " failed: " + ex.Message);
                }
            }

            if (assigned)
            {
                bool shouldPostAssignmentRescue = ShouldAttemptPostAssignmentRescue(
                    attemptedHtmlCandidate,
                    pageUri,
                    assignedCandidate,
                    highestFailedScoreBeforeAssignment);

                if (shouldPostAssignmentRescue)
                {
                    List<FaviconCandidate> rescueCandidates = await TryDiscoverExternalRescueCandidatesAsync(pageUri, cancellationToken, sb).ConfigureAwait(true);
                    List<FaviconCandidate> mergedRescueChain = BuildMergedRescueChain(candidates, rescueCandidates, attemptedUrls);

                    if (mergedRescueChain.Count > 0)
                    {
                        sb.AppendLine("Assigned candidate is tiny/unknown after higher-ranked failures; trying external rescue candidates.");

                        for (int i = 0; i < mergedRescueChain.Count; i++)
                        {
                            FaviconCandidate candidate = mergedRescueChain[i];
                            if (candidate == null || candidate.IconUri == null)
                            {
                                continue;
                            }

                            string candidateUrl = candidate.IconUri.AbsoluteUri;
                            if (!attemptedUrls.Add(candidateUrl))
                            {
                                continue;
                            }

                            if (IsUnsupportedForAssignment(candidate))
                            {
                                sb.AppendLine("Rescue candidate #" + (i + 1) + " skipped: unsupported image format for .NET Framework decoder.");
                                continue;
                            }

                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                byte[] sourceBytes = await FaviconImageDownloader
                                    .DownloadAsync(candidate.IconUri, cancellationToken)
                                    .ConfigureAwait(true);

                                byte[] normalizedPng = IconNormalizer.NormalizeToPng(
                                    sourceBytes,
                                    candidate.TypeAttribute,
                                    candidate.IconUri.AbsoluteUri);

                                cancellationToken.ThrowIfCancellationRequested();

                                PwUuid rescuedUuid = KeePassIconAssigner.AssignNormalizedPngToEntry(
                                    database,
                                    selectedEntry,
                                    normalizedPng);

                                RefreshEntryListIcons(selectedEntry);

                                sb.AppendLine("Assigned custom icon to entry.");
                                sb.AppendLine("Initially assigned from candidate #" + assignedCandidateIndex + ": " + assignedCandidate.IconUri);
                                sb.AppendLine("Replaced with rescue candidate #" + (i + 1) + ": " + candidate.IconUri);
                                sb.AppendLine("Assigned icon UUID: " + rescuedUuid);
                                sb.AppendLine("Normalized PNG size: " + normalizedPng.Length + " bytes");
                                sb.AppendLine();
                                return AssignmentAttemptResult.SuccessResult(normalizedPng);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                lastError = ex;
                                sb.AppendLine("Rescue candidate #" + (i + 1) + " failed: " + ex.Message);
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("No additional external rescue candidates were available.");
                    }
                }

                sb.AppendLine("Assigned custom icon to entry.");
                sb.AppendLine("Assigned from candidate #" + assignedCandidateIndex + ": " + assignedCandidate.IconUri);
                sb.AppendLine("Assigned icon UUID: " + assignedUuid);
                sb.AppendLine("Normalized PNG size: " + assignedNormalizedPngSize + " bytes");
                sb.AppendLine();
                return AssignmentAttemptResult.SuccessResult(assignedNormalizedPngBytes);
            }

            if (ShouldAttemptExternalRescue(attemptedHtmlCandidate, pageUri))
            {
                List<FaviconCandidate> rescueCandidates = await TryDiscoverExternalRescueCandidatesAsync(pageUri, cancellationToken, sb).ConfigureAwait(true);
                List<FaviconCandidate> mergedRescueChain = BuildMergedRescueChain(candidates, rescueCandidates, attemptedUrls);

                if (mergedRescueChain.Count > 0)
                {
                    sb.AppendLine("Primary HTML candidates failed during assignment; trying external rescue candidates.");

                    for (int i = 0; i < mergedRescueChain.Count; i++)
                    {
                        FaviconCandidate candidate = mergedRescueChain[i];
                        if (candidate == null || candidate.IconUri == null)
                        {
                            continue;
                        }

                        string candidateUrl = candidate.IconUri.AbsoluteUri;
                        if (!attemptedUrls.Add(candidateUrl))
                        {
                            continue;
                        }

                        if (IsUnsupportedForAssignment(candidate))
                        {
                            sb.AppendLine("Rescue candidate #" + (i + 1) + " skipped: unsupported image format for .NET Framework decoder.");
                            continue;
                        }

                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            byte[] sourceBytes = await FaviconImageDownloader
                                .DownloadAsync(candidate.IconUri, cancellationToken)
                                .ConfigureAwait(true);

                            byte[] normalizedPng = IconNormalizer.NormalizeToPng(
                                sourceBytes,
                                candidate.TypeAttribute,
                                candidate.IconUri.AbsoluteUri);

                            cancellationToken.ThrowIfCancellationRequested();

                            PwUuid rescueAssignedUuid = KeePassIconAssigner.AssignNormalizedPngToEntry(
                                database,
                                selectedEntry,
                                normalizedPng);

                            RefreshEntryListIcons(selectedEntry);

                            sb.AppendLine("Assigned custom icon to entry.");
                            sb.AppendLine("Assigned from rescue candidate #" + (i + 1) + ": " + candidate.IconUri);
                            sb.AppendLine("Assigned icon UUID: " + rescueAssignedUuid);
                            sb.AppendLine("Normalized PNG size: " + normalizedPng.Length + " bytes");
                            sb.AppendLine();
                            return AssignmentAttemptResult.SuccessResult(normalizedPng);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            sb.AppendLine("Rescue candidate #" + (i + 1) + " failed: " + ex.Message);
                        }
                    }
                }
                else
                {
                    sb.AppendLine("Primary HTML candidates failed and no additional external rescue candidates were available.");
                }
            }

            sb.AppendLine("Assignment failed: all candidates failed.");
            sb.AppendLine();

            return AssignmentAttemptResult.Fail(lastError != null ? lastError.Message : "All candidate downloads failed.");
        }

        private static bool ShouldAttemptExternalRescue(bool attemptedHtmlCandidate, Uri pageUri)
        {
            return attemptedHtmlCandidate && pageUri != null;
        }

        private static bool ShouldAttemptPostAssignmentRescue(bool attemptedHtmlCandidate, Uri pageUri, FaviconCandidate assignedCandidate, int highestFailedScoreBeforeAssignment)
        {
            if (!ShouldAttemptExternalRescue(attemptedHtmlCandidate, pageUri))
            {
                return false;
            }

            if (assignedCandidate == null)
            {
                return false;
            }

            bool assignedLooksTinyOrUnknown = !assignedCandidate.BestSize.HasValue
                || Math.Max(assignedCandidate.BestSize.Value.Width, assignedCandidate.BestSize.Value.Height) <= 32;

            if (!assignedLooksTinyOrUnknown)
            {
                return false;
            }

            return highestFailedScoreBeforeAssignment > assignedCandidate.Score;
        }

        private static async System.Threading.Tasks.Task<List<FaviconCandidate>> TryDiscoverExternalRescueCandidatesAsync(Uri pageUri, CancellationToken cancellationToken, StringBuilder sb)
        {
            if (pageUri == null)
            {
                return new List<FaviconCandidate>();
            }

            try
            {
                using (CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    List<FaviconCandidate> externalCandidates = await ExternalFaviconServiceDiscoverer
                        .DiscoverAsync(pageUri, cts.Token)
                        .ConfigureAwait(false);

                    return externalCandidates ?? new List<FaviconCandidate>();
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine("External rescue discovery timed out.");
                return new List<FaviconCandidate>();
            }
            catch (Exception ex)
            {
                sb.AppendLine("External rescue discovery failed: " + ex.Message);
                return new List<FaviconCandidate>();
            }
        }

        private static List<FaviconCandidate> BuildMergedRescueChain(IReadOnlyList<FaviconCandidate> currentCandidates, IReadOnlyList<FaviconCandidate> rescueCandidates, ISet<string> attemptedUrls)
        {
            IEnumerable<FaviconCandidate> merged = EnumerateCandidates(currentCandidates)
                .Concat(EnumerateCandidates(rescueCandidates));

            return merged
                .Where(c => c != null && c.IconUri != null)
                .GroupBy(c => c.IconUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.IconUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Where(c => attemptedUrls == null || !attemptedUrls.Contains(c.IconUri.AbsoluteUri))
                .ToList();
        }

        private static IEnumerable<FaviconCandidate> EnumerateCandidates(IReadOnlyList<FaviconCandidate> candidates)
        {
            return candidates ?? Enumerable.Empty<FaviconCandidate>();
        }

        private static bool IsHtmlCandidate(FaviconCandidate candidate)
        {
            return candidate != null
                && string.Equals(candidate.Source, "html-link", StringComparison.OrdinalIgnoreCase);
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

        private static bool IsUnsupportedForAssignment(FaviconCandidate candidate)
        {
            if (candidate == null)
            {
                return true;
            }

            string type = candidate.TypeAttribute;
            if (!string.IsNullOrWhiteSpace(type) && type.IndexOf("image/avif", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string url = candidate.IconUri != null ? candidate.IconUri.AbsoluteUri : string.Empty;
            return !string.IsNullOrWhiteSpace(url) && url.EndsWith(".avif", StringComparison.OrdinalIgnoreCase);
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

        private sealed class AssignmentAttemptResult
        {
            public bool Success { get; private set; }
            public string FailureReason { get; private set; }
            public byte[] AssignedIconPngBytes { get; private set; }

            private AssignmentAttemptResult()
            {
            }

            public static AssignmentAttemptResult SuccessResult(byte[] assignedIconPngBytes)
            {
                return new AssignmentAttemptResult
                {
                    Success = true,
                    AssignedIconPngBytes = assignedIconPngBytes
                };
            }

            public static AssignmentAttemptResult Fail(string failureReason)
            {
                return new AssignmentAttemptResult
                {
                    Success = false,
                    FailureReason = failureReason
                };
            }
        }

        private sealed class ExtractionStatusForm : Form
        {
            private readonly TextBox outputTextBox;
            private readonly PictureBox iconPreviewBox;
            private readonly Button cancelButton;
            private readonly Button closeButton;
            private readonly System.Windows.Forms.Timer closeCountdownTimer;
            private Action cancelAction;
            private int countdownSeconds;

            public ExtractionStatusForm()
            {
                Text = "FaviconExtractor";
                Width = 620;
                Height = 300;
                StartPosition = FormStartPosition.Manual;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                outputTextBox = new TextBox();
                outputTextBox.Multiline = true;
                outputTextBox.ReadOnly = true;
                outputTextBox.ScrollBars = ScrollBars.Vertical;
                outputTextBox.WordWrap = true;
                outputTextBox.Dock = DockStyle.Fill;

                iconPreviewBox = new PictureBox();
                iconPreviewBox.Width = 72;
                iconPreviewBox.Height = 72;
                iconPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
                iconPreviewBox.BorderStyle = BorderStyle.FixedSingle;
                iconPreviewBox.Margin = new Padding(0, 0, 8, 0);

                FlowLayoutPanel contentPanel = new FlowLayoutPanel();
                contentPanel.Dock = DockStyle.Fill;
                contentPanel.FlowDirection = FlowDirection.LeftToRight;
                contentPanel.WrapContents = false;
                contentPanel.Padding = new Padding(8, 8, 8, 0);

                outputTextBox.Width = Width - 120;
                outputTextBox.Height = Height - 80;

                contentPanel.Controls.Add(iconPreviewBox);
                contentPanel.Controls.Add(outputTextBox);

                FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
                buttonPanel.Dock = DockStyle.Bottom;
                buttonPanel.FlowDirection = FlowDirection.RightToLeft;
                buttonPanel.Height = 40;
                buttonPanel.Padding = new Padding(8, 6, 8, 6);

                closeButton = new Button();
                closeButton.Text = "OK";
                closeButton.Width = 100;
                closeButton.Enabled = false;
                closeButton.Click += (_, __) => Close();

                cancelButton = new Button();
                cancelButton.Text = "Cancel";
                cancelButton.Width = 100;
                cancelButton.Click += OnCancelClick;

                buttonPanel.Controls.Add(closeButton);
                buttonPanel.Controls.Add(cancelButton);

                closeCountdownTimer = new System.Windows.Forms.Timer();
                closeCountdownTimer.Interval = 1000;
                closeCountdownTimer.Tick += OnCloseCountdownTick;

                Controls.Add(contentPanel);
                Controls.Add(buttonPanel);
            }

            public void SetAssignedIconPreview(byte[] pngBytes)
            {
                if (pngBytes == null || pngBytes.Length == 0 || IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action<byte[]>(SetAssignedIconPreview), pngBytes);
                    return;
                }

                Image previous = iconPreviewBox.Image;
                using (MemoryStream ms = new MemoryStream(pngBytes))
                using (Image source = Image.FromStream(ms))
                {
                    iconPreviewBox.Image = new Bitmap(source);
                }

                if (previous != null)
                {
                    previous.Dispose();
                }
            }

            public void AttachCancelAction(Action action)
            {
                cancelAction = action;
            }

            public void PositionNearOwner(Form owner)
            {
                if (owner == null)
                {
                    StartPosition = FormStartPosition.CenterScreen;
                    return;
                }

                int x = owner.Left + ((owner.Width - Width) / 2);
                int y = owner.Top + ((owner.Height - Height) / 2);

                if (x < 0) x = 0;
                if (y < 0) y = 0;

                Location = new System.Drawing.Point(x, y);
            }

            public void AppendLineSafe(string line)
            {
                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action<string>(AppendLineSafe), line);
                    return;
                }

                outputTextBox.AppendText(line + Environment.NewLine);
            }

            public void MarkCompletedWithCountdown(int seconds)
            {
                if (seconds < 0) seconds = 0;

                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action<int>(MarkCompletedWithCountdown), seconds);
                    return;
                }

                cancelButton.Enabled = false;
                closeButton.Enabled = true;

                countdownSeconds = seconds;
                UpdateCloseButtonCountdownText();

                if (countdownSeconds == 0)
                {
                    Close();
                    return;
                }

                closeCountdownTimer.Start();
            }

            public void MarkFailed()
            {
                SetTerminalWithoutAutoClose();
            }

            public void MarkCanceled()
            {
                SetTerminalWithoutAutoClose();
            }

            private void SetTerminalWithoutAutoClose()
            {
                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action(SetTerminalWithoutAutoClose));
                    return;
                }

                closeCountdownTimer.Stop();
                cancelButton.Enabled = false;
                closeButton.Text = "OK";
                closeButton.Enabled = true;
            }

            private void OnCancelClick(object sender, EventArgs e)
            {
                cancelButton.Enabled = false;
                AppendLineSafe("Cancel requested...");
                Action action = cancelAction;
                if (action != null)
                {
                    action();
                }
            }

            private void OnCloseCountdownTick(object sender, EventArgs e)
            {
                countdownSeconds--;
                if (countdownSeconds <= 0)
                {
                    closeCountdownTimer.Stop();
                    Close();
                    return;
                }

                UpdateCloseButtonCountdownText();
            }

            private void UpdateCloseButtonCountdownText()
            {
                closeButton.Text = countdownSeconds > 0
                    ? "OK (" + countdownSeconds + ")"
                    : "OK";
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                closeCountdownTimer.Stop();

                Image image = iconPreviewBox.Image;
                if (image != null)
                {
                    iconPreviewBox.Image = null;
                    image.Dispose();
                }

                base.OnFormClosed(e);
            }
        }

        private sealed class DiagnosticsProgressForm : Form
        {
            private readonly TextBox outputTextBox;
            private readonly Button closeButton;

            public DiagnosticsProgressForm()
            {
                Text = "FaviconExtractor Diagnostics";
                Width = 760;
                Height = 480;
                StartPosition = FormStartPosition.Manual;

                outputTextBox = new TextBox();
                outputTextBox.Multiline = true;
                outputTextBox.ReadOnly = true;
                outputTextBox.ScrollBars = ScrollBars.Vertical;
                outputTextBox.WordWrap = false;
                outputTextBox.Dock = DockStyle.Fill;

                closeButton = new Button();
                closeButton.Text = "Close";
                closeButton.Dock = DockStyle.Bottom;
                closeButton.Height = 34;
                closeButton.Enabled = false;
                closeButton.Click += (_, __) => Close();

                Controls.Add(outputTextBox);
                Controls.Add(closeButton);
            }

            public void PositionNearOwner(Form owner)
            {
                if (owner == null)
                {
                    StartPosition = FormStartPosition.CenterScreen;
                    return;
                }

                int x = owner.Left + ((owner.Width - Width) / 2);
                int y = owner.Top + ((owner.Height - Height) / 2);

                if (x < 0) x = 0;
                if (y < 0) y = 0;

                Location = new System.Drawing.Point(x, y);
            }

            public void AppendLineSafe(string line)
            {
                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action<string>(AppendLineSafe), line);
                    return;
                }

                outputTextBox.AppendText(line + Environment.NewLine);
            }

            public void MarkCompleted()
            {
                AppendLineSafe(string.Empty);
                AppendLineSafe("Diagnostics completed.");
                EnableClosing();
            }

            public void MarkFailed(string message)
            {
                AppendLineSafe(string.Empty);
                AppendLineSafe("Diagnostics failed: " + message);
                EnableClosing();
            }

            private void EnableClosing()
            {
                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action(EnableClosing));
                    return;
                }

                closeButton.Enabled = true;
            }
        }
    }
}
