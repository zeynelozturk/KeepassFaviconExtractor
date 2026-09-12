using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
        private Icon dialogIcon;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public override bool Initialize(IPluginHost pluginHost)
        {
            if (pluginHost == null)
            {
                return false;
            }

            host = pluginHost;
            return true;
        }

        public override string UpdateUrl
        {
            get { return "https://raw.githubusercontent.com/zeynelozturk/KeepassFaviconExtractor/master/update.txt"; }
        }

        public override ToolStripMenuItem GetMenuItem(PluginMenuType type)
        {
            if (type == PluginMenuType.Main)
            {
                ToolStripMenuItem root = new ToolStripMenuItem("Favicon Extractor");
                // set small icon from Assets (if available)
                Image icon = LoadMenuIcon();
                if (icon != null)
                {
                    root.Image = icon;
                }

                root.DropDownItems.Add(CreateMenuItem("Extract website favicon", OnExtractFaviconClick));
                root.DropDownItems.Add(CreateMenuItem("Diagnostics", OnDiagnosticsMenuItemClick));
                return root;
            }

            if (type == PluginMenuType.Entry)
            {
                return CreateMenuItem("Extract website favicon", OnExtractFaviconClick, LoadMenuIcon());
            }

            return null;
        }

        public override void Terminate()
        {
            host = null;
        }

        private ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick, Image icon = null)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            if (icon != null)
            {
                item.Image = icon;
            }
            item.Click += onClick;
            return item;
        }

        private Image menuIcon;
        private Image LoadMenuIcon()
        {
            if (menuIcon != null)
                return menuIcon;

            // Prefer the embedded resource first (if it was packaged into the DLL).
            const string preferredFileName = "FaviconExtractorIcon_small.png";
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] resources = asm.GetManifestResourceNames();
                string match = resources.FirstOrDefault(r => r.EndsWith(preferredFileName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match))
                {
                    using (Stream rs = asm.GetManifestResourceStream(match))
                    using (Image img = Image.FromStream(rs))
                    {
                        menuIcon = new Bitmap(img, new Size(16, 16));
                        return menuIcon;
                    }
                }
            }
            catch
            {
                // ignore and fall back to filesystem search
            }

            // If not embedded, search upward from several likely base directories and look for an Assets folder containing the preferred file.
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            string[] baseDirs = new[] { assemblyDir, AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() };

            foreach (string baseDir in baseDirs)
            {
                try
                {
                    DirectoryInfo dir = new DirectoryInfo(baseDir);
                    int levels = 0;
                    while (dir != null && levels < 6)
                    {
                        string candidate = Path.Combine(dir.FullName, "Assets", preferredFileName);
                        if (File.Exists(candidate))
                        {
                            try
                            {
                                using (FileStream fs = File.OpenRead(candidate))
                                using (Image img = Image.FromStream(fs))
                                {
                                    menuIcon = new Bitmap(img, new Size(16, 16));
                                    return menuIcon;
                                }
                            }
                            catch
                            {
                                // fallthrough to continue searching
                            }
                        }

                        dir = dir.Parent;
                        levels++;
                    }
                }
                catch
                {
                    // ignore and try next baseDir
                }
            }

            return null;
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

            ExtractionStatusForm statusForm = new ExtractionStatusForm();
            Icon windowIcon = GetDialogIcon();
            if (windowIcon != null)
            {
                statusForm.Icon = windowIcon;
                statusForm.ShowIcon = true;
            }
            statusForm.PositionNearOwner(host.MainWindow as Form);
            statusForm.AttachCancelAction(() =>
            {
                CancellationTokenSource cts = extractCancellationTokenSource;
                if (cts != null && !cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            });
            statusForm.AttachRetryAction(() =>
            {
                if (isExtractRunning)
                {
                    return;
                }

                _ = RunExtractFaviconWorkflowAsync(statusForm);
            });
            statusForm.Show(host.MainWindow);

            await RunExtractFaviconWorkflowAsync(statusForm).ConfigureAwait(true);
        }

        private async System.Threading.Tasks.Task RunExtractFaviconWorkflowAsync(ExtractionStatusForm statusForm)
        {
            if (statusForm == null || statusForm.IsDisposed)
            {
                return;
            }

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
            statusForm.BeginProgress();

            try
            {
                PwEntry selectedEntry = host.MainWindow.GetSelectedEntry(false);
                if (selectedEntry == null)
                {
                    statusForm.AppendLineSafe(string.Empty);
                    statusForm.AppendLineSafe("Extraction failed.");
                    statusForm.AppendLineSafe("Reason: Select an entry first.");
                    statusForm.MarkFailed();
                    return;
                }

                string url = selectedEntry.Strings.ReadSafe(PwDefs.UrlField);
                if (string.IsNullOrWhiteSpace(url))
                {
                    // Prompt user for URL when entry has none
                    using (PromptDialog dialog = new PromptDialog())
                    {
                        dialog.Icon = GetDialogIcon();
                        DialogResult promptResult = dialog.ShowDialog(host.MainWindow);
                        if (promptResult != DialogResult.OK)
                        {
                            // User cancelled - close status form without error
                            statusForm.Close();
                            return;
                        }
                        url = dialog.PromptedUrl;
                    }

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        statusForm.AppendLineSafe(string.Empty);
                        statusForm.AppendLineSafe("Extraction failed.");
                        statusForm.AppendLineSafe("Reason: No URL was provided.");
                        statusForm.MarkFailed();
                        return;
                    }
                }

                CancellationToken cancellationToken = extractCancellationTokenSource.Token;

                statusForm.AppendLineSafe("Searching icon candidates...");
                HtmlFaviconDiscoveryResult result = await FaviconDiscoveryService
                    .DiscoverAsync(url, cancellationToken, statusForm.AppendLineSafe)
                    .ConfigureAwait(true);

                cancellationToken.ThrowIfCancellationRequested();

                if (result == null || result.Candidates == null || result.Candidates.Count == 0)
                {
                    statusForm.AppendLineSafe(string.Empty);
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
                AssignmentAttemptResult assignmentResult;
                using (CancellationTokenSource assignmentTimeoutCts = new CancellationTokenSource(FaviconDiscoveryPreferences.AssignmentTimeout))
                using (CancellationTokenSource assignmentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, assignmentTimeoutCts.Token))
                {
                    try
                    {
                        assignmentResult = await TryAssignCandidatesToEntryAsync(
                            selectedEntry,
                            result.Candidates,
                            result.PageUri,
                            cancellationToken,
                            assignmentCts.Token,
                            new StringBuilder(),
                            statusForm.AppendLineSafe).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        assignmentResult = AssignmentAttemptResult.Fail("Timed out while downloading/assigning icon.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (assignmentResult.Success)
                {
                    statusForm.SetAssignedIconPreview(assignmentResult.AssignedIconPngBytes);
                    if (!string.IsNullOrWhiteSpace(assignmentResult.WarningMessage))
                    {
                        statusForm.AppendLineSafe("Warning: " + assignmentResult.WarningMessage);
                    }
                    statusForm.AppendLineSafe(string.Empty);
                    statusForm.AppendLineSafe("Success: icon assigned.");
                    statusForm.MarkCompletedWithCountdown(3);
                    return;
                }

                statusForm.AppendLineSafe(string.Empty);
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
                statusForm.AppendLineSafe(string.Empty);
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
            Icon windowIcon = GetDialogIcon();
            if (windowIcon != null)
            {
                diagnosticsForm.Icon = windowIcon;
                diagnosticsForm.ShowIcon = true;
            }

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

        private Icon GetDialogIcon()
        {
            if (dialogIcon != null)
            {
                return dialogIcon;
            }

            try
            {
                Image image = LoadMenuIcon();
                if (image == null)
                {
                    return null;
                }

                using (Bitmap bitmap = new Bitmap(image))
                {
                    IntPtr hIcon = bitmap.GetHicon();
                    try
                    {
                        using (Icon sourceIcon = Icon.FromHandle(hIcon))
                        {
                            dialogIcon = (Icon)sourceIcon.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
            catch
            {
                dialogIcon = null;
            }

            return dialogIcon;
        }

        private async System.Threading.Tasks.Task<AssignmentAttemptResult> TryAssignCandidatesToEntryAsync(PwEntry selectedEntry, IReadOnlyList<FaviconCandidate> candidates, Uri pageUri, CancellationToken userCancellationToken, CancellationToken cancellationToken, StringBuilder sb, Action<string> onStatus)
        {
            PwDatabase database = host.Database;
            if (database == null || !database.IsOpen)
            {
                return AssignmentAttemptResult.Fail("No open KeePass database.");
            }

            ReportStatus(onStatus, "Candidates found: " + candidates.Count);

            int maxAttempts = Math.Max(1, FaviconDiscoveryPreferences.MaxAssignmentAttempts);
            int attemptedCount = 0;
            bool hitAttemptLimit = false;

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
                    ReportStatus(onStatus, "Candidate #" + (i + 1) + " skipped (unsupported format).");
                    continue;
                }

                if (attemptedCount >= maxAttempts)
                {
                    hitAttemptLimit = true;
                    break;
                }

                try
                {
                    ReportStatus(onStatus, "Candidate #" + (i + 1) + " info: source="
                        + (string.IsNullOrWhiteSpace(candidate.Source) ? "unknown" : candidate.Source)
                        + ", type=" + (string.IsNullOrWhiteSpace(candidate.TypeAttribute) ? "(none)" : candidate.TypeAttribute)
                        + ", url=" + candidate.IconUri.AbsoluteUri);
                    ReportStatus(onStatus, "Trying candidate #" + (i + 1) + "...");
                    attemptedCount++;
                    cancellationToken.ThrowIfCancellationRequested();

                    Stopwatch downloadStopwatch = Stopwatch.StartNew();
                    ReportStatus(onStatus, "Candidate #" + (i + 1) + " downloading...");
                    byte[] sourceBytes = await FaviconImageDownloader
                        .DownloadAsync(candidate.IconUri, cancellationToken)
                        .ConfigureAwait(true);
                    downloadStopwatch.Stop();
                    ReportStatus(onStatus, "Candidate #" + (i + 1) + " download completed (" + downloadStopwatch.ElapsedMilliseconds + "ms).");

                    bool candidateIsSvg = IsLikelySvgCandidate(candidate, sourceBytes);
                    if (candidateIsSvg)
                    {
                        ReportStatus(onStatus, "Candidate #" + (i + 1) + " identified as SVG source.");
                    }

                    Stopwatch normalizeAssignStopwatch = Stopwatch.StartNew();
                    ReportStatus(onStatus, "Candidate #" + (i + 1) + " normalizing/assigning...");
                    AssignmentExecutionResult assignedResult = await ExecuteNormalizeAndAssignAsync(
                        database,
                        selectedEntry,
                        sourceBytes,
                        candidate.TypeAttribute,
                        candidate.IconUri.AbsoluteUri,
                        cancellationToken).ConfigureAwait(true);
                    normalizeAssignStopwatch.Stop();
                    ReportStatus(onStatus, "Candidate #" + (i + 1) + " normalize/assign completed (" + normalizeAssignStopwatch.ElapsedMilliseconds + "ms).");

                    RefreshEntryListIcons(selectedEntry);

                    assigned = true;
                    assignedCandidate = candidate;
                    assignedCandidateIndex = i + 1;
                    assignedNormalizedPngSize = assignedResult.NormalizedPng.Length;
                    assignedNormalizedPngBytes = assignedResult.NormalizedPng;
                    assignedUuid = assignedResult.AssignedUuid;
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
                    ReportStatus(onStatus, "Candidate #" + (i + 1) + " failed: " + ex.GetType().Name + " - " + ex.Message);
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
                    ReportStatus(onStatus, "Checking rescue candidates...");
                    List<FaviconCandidate> rescueCandidates = await TryDiscoverExternalRescueCandidatesAsync(pageUri, cancellationToken, sb, onStatus).ConfigureAwait(true);
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
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " skipped (unsupported format).");
                                continue;
                            }

                            if (attemptedCount >= maxAttempts)
                            {
                                hitAttemptLimit = true;
                                break;
                            }

                            try
                            {
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " info: source="
                                    + (string.IsNullOrWhiteSpace(candidate.Source) ? "unknown" : candidate.Source)
                                    + ", type=" + (string.IsNullOrWhiteSpace(candidate.TypeAttribute) ? "(none)" : candidate.TypeAttribute)
                                    + ", url=" + candidate.IconUri.AbsoluteUri);
                                ReportStatus(onStatus, "Trying rescue candidate #" + (i + 1) + "...");
                                attemptedCount++;
                                cancellationToken.ThrowIfCancellationRequested();

                                Stopwatch downloadStopwatch = Stopwatch.StartNew();
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " downloading...");
                                byte[] sourceBytes = await FaviconImageDownloader
                                    .DownloadAsync(candidate.IconUri, cancellationToken)
                                    .ConfigureAwait(true);
                                downloadStopwatch.Stop();
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " download completed (" + downloadStopwatch.ElapsedMilliseconds + "ms).");

                                bool rescueCandidateIsSvg = IsLikelySvgCandidate(candidate, sourceBytes);
                                if (rescueCandidateIsSvg)
                                {
                                    ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " identified as SVG source.");
                                }

                                Stopwatch normalizeAssignStopwatch = Stopwatch.StartNew();
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " normalizing/assigning...");
                                AssignmentExecutionResult assignedResult = await ExecuteNormalizeAndAssignAsync(
                                    database,
                                    selectedEntry,
                                    sourceBytes,
                                    candidate.TypeAttribute,
                                    candidate.IconUri.AbsoluteUri,
                                    cancellationToken).ConfigureAwait(true);
                                normalizeAssignStopwatch.Stop();
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " normalize/assign completed (" + normalizeAssignStopwatch.ElapsedMilliseconds + "ms).");

                                RefreshEntryListIcons(selectedEntry);

                                sb.AppendLine("Assigned custom icon to entry.");
                                sb.AppendLine("Initially assigned from candidate #" + assignedCandidateIndex + ": " + assignedCandidate.IconUri);
                                sb.AppendLine("Replaced with rescue candidate #" + (i + 1) + ": " + candidate.IconUri);
                                sb.AppendLine("Assigned icon UUID: " + assignedResult.AssignedUuid);
                                sb.AppendLine("Normalized PNG size: " + assignedResult.NormalizedPng.Length + " bytes");
                                sb.AppendLine();
                                ReportStatus(onStatus, "Assigned from rescue candidate #" + (i + 1) + ".");
                                if (rescueCandidateIsSvg)
                                {
                                    ReportStatus(onStatus, "Assigned icon came from SVG conversion.");
                                }
                                return AssignmentAttemptResult.SuccessResult(assignedResult.NormalizedPng);
                            }
                            catch (OperationCanceledException)
                            {
                                if (!userCancellationToken.IsCancellationRequested && assignedNormalizedPngBytes != null)
                                {
                                    ReportStatus(onStatus, "Rescue canceled/timed out. Keeping assigned icon.");
                                    return AssignmentAttemptResult.SuccessResult(
                                        assignedNormalizedPngBytes,
                                        "Rescue attempt timed out or was canceled; kept initially assigned icon.");
                                }

                                throw;
                            }
                            catch (Exception ex)
                            {
                                lastError = ex;
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " failed: " + ex.GetType().Name + " - " + ex.Message);
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
                ReportStatus(onStatus, "Assigned from candidate #" + assignedCandidateIndex + ".");
                return AssignmentAttemptResult.SuccessResult(assignedNormalizedPngBytes);
            }

            if (ShouldAttemptExternalRescue(attemptedHtmlCandidate, pageUri))
            {
                ReportStatus(onStatus, "Checking rescue candidates...");
                List<FaviconCandidate> rescueCandidates = await TryDiscoverExternalRescueCandidatesAsync(pageUri, cancellationToken, sb, onStatus).ConfigureAwait(true);
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
                            ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " skipped (unsupported format).");
                            continue;
                        }

                        if (attemptedCount >= maxAttempts)
                        {
                            hitAttemptLimit = true;
                            break;
                        }

                        try
                        {
                            ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " info: source="
                                + (string.IsNullOrWhiteSpace(candidate.Source) ? "unknown" : candidate.Source)
                                + ", type=" + (string.IsNullOrWhiteSpace(candidate.TypeAttribute) ? "(none)" : candidate.TypeAttribute)
                                + ", url=" + candidate.IconUri.AbsoluteUri);
                            ReportStatus(onStatus, "Trying rescue candidate #" + (i + 1) + "...");
                            attemptedCount++;
                            cancellationToken.ThrowIfCancellationRequested();

                            Stopwatch downloadStopwatch = Stopwatch.StartNew();
                            ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " downloading...");
                            byte[] sourceBytes = await FaviconImageDownloader
                                .DownloadAsync(candidate.IconUri, cancellationToken)
                                .ConfigureAwait(true);
                            downloadStopwatch.Stop();
                            ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " download completed (" + downloadStopwatch.ElapsedMilliseconds + "ms).");

                            bool rescueCandidateIsSvg = IsLikelySvgCandidate(candidate, sourceBytes);
                            if (rescueCandidateIsSvg)
                            {
                                ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " identified as SVG source.");
                            }

                            Stopwatch normalizeAssignStopwatch = Stopwatch.StartNew();
                            ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " normalizing/assigning...");
                            AssignmentExecutionResult assignedResult = await ExecuteNormalizeAndAssignAsync(
                                database,
                                selectedEntry,
                                sourceBytes,
                                candidate.TypeAttribute,
                                candidate.IconUri.AbsoluteUri,
                                cancellationToken).ConfigureAwait(true);
                            normalizeAssignStopwatch.Stop();
                            ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " normalize/assign completed (" + normalizeAssignStopwatch.ElapsedMilliseconds + "ms).");

                            RefreshEntryListIcons(selectedEntry);

                            sb.AppendLine("Assigned custom icon to entry.");
                            sb.AppendLine("Assigned from rescue candidate #" + (i + 1) + ": " + candidate.IconUri);
                            sb.AppendLine("Assigned icon UUID: " + assignedResult.AssignedUuid);
                            sb.AppendLine("Normalized PNG size: " + assignedResult.NormalizedPng.Length + " bytes");
                            sb.AppendLine();
                            ReportStatus(onStatus, "Assigned from rescue candidate #" + (i + 1) + ".");
                            if (rescueCandidateIsSvg)
                            {
                                ReportStatus(onStatus, "Assigned icon came from SVG conversion.");
                            }
                            return AssignmentAttemptResult.SuccessResult(assignedResult.NormalizedPng);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            ReportStatus(onStatus, "Rescue candidate #" + (i + 1) + " failed: " + ex.GetType().Name + " - " + ex.Message);
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

            if (hitAttemptLimit)
            {
                ReportStatus(onStatus, "Attempt limit reached.");
                return AssignmentAttemptResult.Fail("Reached assignment attempt limit (" + maxAttempts + ").");
            }

            return AssignmentAttemptResult.Fail(lastError != null ? lastError.Message : "All candidate downloads failed.");
        }

        private static async System.Threading.Tasks.Task<AssignmentExecutionResult> ExecuteNormalizeAndAssignAsync(
            PwDatabase database,
            PwEntry selectedEntry,
            byte[] sourceBytes,
            string typeAttribute,
            string iconUrl,
            CancellationToken cancellationToken)
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] normalizedPng;
                try
                {
                    normalizedPng = IconNormalizer.NormalizeToPng(sourceBytes, typeAttribute, iconUrl, cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Normalize failed: " + ex.Message, ex);
                }

                cancellationToken.ThrowIfCancellationRequested();

                PwUuid assignedUuid;
                try
                {
                    assignedUuid = KeePassIconAssigner.AssignNormalizedPngToEntry(database, selectedEntry, normalizedPng);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Assign failed: " + ex.Message, ex);
                }

                return new AssignmentExecutionResult
                {
                    NormalizedPng = normalizedPng,
                    AssignedUuid = assignedUuid
                };
            }, cancellationToken).ConfigureAwait(false);
        }

        private static bool ShouldAttemptExternalRescue(bool attemptedHtmlCandidate, Uri pageUri)
        {
            return attemptedHtmlCandidate && pageUri != null;
        }

        private sealed class AssignmentExecutionResult
        {
            public byte[] NormalizedPng { get; set; }
            public PwUuid AssignedUuid { get; set; }
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

        private static async System.Threading.Tasks.Task<List<FaviconCandidate>> TryDiscoverExternalRescueCandidatesAsync(Uri pageUri, CancellationToken cancellationToken, StringBuilder sb, Action<string> onStatus)
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
                ReportStatus(onStatus, "Rescue discovery timed out.");
                return new List<FaviconCandidate>();
            }
            catch (Exception ex)
            {
                sb.AppendLine("External rescue discovery failed: " + ex.Message);
                ReportStatus(onStatus, "Rescue discovery failed.");
                return new List<FaviconCandidate>();
            }
        }

        private static void ReportStatus(Action<string> onStatus, string line)
        {
            if (onStatus != null && !string.IsNullOrWhiteSpace(line))
            {
                onStatus(line);
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

        private static bool IsLikelySvgCandidate(FaviconCandidate candidate, byte[] sourceBytes)
        {
            if (candidate != null && !string.IsNullOrWhiteSpace(candidate.TypeAttribute) &&
                candidate.TypeAttribute.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (candidate != null && candidate.IconUri != null &&
                candidate.IconUri.AbsoluteUri.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (sourceBytes == null || sourceBytes.Length == 0)
            {
                return false;
            }

            int count = Math.Min(sourceBytes.Length, 512);
            string prefix = Encoding.UTF8.GetString(sourceBytes, 0, count);
            return prefix.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0;
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
            public string WarningMessage { get; private set; }

            private AssignmentAttemptResult()
            {
            }

            public static AssignmentAttemptResult SuccessResult(byte[] assignedIconPngBytes, string warningMessage = null)
            {
                return new AssignmentAttemptResult
                {
                    Success = true,
                    AssignedIconPngBytes = assignedIconPngBytes,
                    WarningMessage = warningMessage
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

        private sealed class PromptDialog : Form
        {
            private readonly TextBox urlTextBox;
            public string PromptedUrl { get; private set; }

            public PromptDialog()
            {
                Text = "Extract Favicon";
                Width = 400;
                Height = 160;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                Label promptLabel = new Label
                {
                    Text = "The selected entry has no URL. Please provide one:",
                    AutoSize = true,
                    Location = new System.Drawing.Point(12, 12)
                };

                urlTextBox = new TextBox
                {
                    Text = "https://example.com",
                    ForeColor = System.Drawing.SystemColors.GrayText,
                    Location = new System.Drawing.Point(12, 36),
                    Width = 360,
                    Height = 20
                };

                urlTextBox.GotFocus += (s, e) =>
                {
                    if (urlTextBox.Text == "https://example.com" && urlTextBox.ForeColor == System.Drawing.SystemColors.GrayText)
                    {
                        urlTextBox.Text = string.Empty;
                        urlTextBox.ForeColor = System.Drawing.SystemColors.WindowText;
                    }
                };

                urlTextBox.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(urlTextBox.Text))
                    {
                        urlTextBox.Text = "https://example.com";
                        urlTextBox.ForeColor = System.Drawing.SystemColors.GrayText;
                    }
                };

                Button okButton = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new System.Drawing.Point(216, 72),
                    Width = 75
                };
                okButton.Click += (s, e) => OnOkClick();

                Button cancelButton = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new System.Drawing.Point(297, 72),
                    Width = 75
                };

                Controls.Add(promptLabel);
                Controls.Add(urlTextBox);
                Controls.Add(okButton);
                Controls.Add(cancelButton);

                AcceptButton = okButton;
                CancelButton = cancelButton;
            }

            private void OnOkClick()
            {
                string input = urlTextBox.Text;
                if (input == "https://example.com" && urlTextBox.ForeColor == System.Drawing.SystemColors.GrayText)
                {
                    input = string.Empty;
                }
                PromptedUrl = input;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private sealed class ExtractionStatusForm : Form
        {
            private readonly TextBox outputTextBox;
            private readonly RoundedPictureBox iconPreviewBox;
            private readonly Label iconSizeLabel;
            private readonly Button cancelButton;
            private readonly Button retryButton;
            private readonly Button closeButton;
            private readonly ContextMenuStrip outputContextMenu;
            private readonly System.Windows.Forms.Timer closeCountdownTimer;
            private Action cancelAction;
            private Action retryAction;
            private int countdownSeconds;
            private bool isInProgress;
            private bool isOutputContextMenuOpen;
            private bool pendingAutoClose;

            public ExtractionStatusForm()
            {
                Text = "Favicon Extractor";
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

                outputContextMenu = new ContextMenuStrip();
                ToolStripMenuItem copyMenuItem = new ToolStripMenuItem("Copy");
                copyMenuItem.Click += (_, __) =>
                {
                    if (!string.IsNullOrEmpty(outputTextBox.SelectedText))
                    {
                        Clipboard.SetText(outputTextBox.SelectedText);
                    }
                };

                ToolStripMenuItem selectAllMenuItem = new ToolStripMenuItem("Select All");
                selectAllMenuItem.Click += (_, __) => outputTextBox.SelectAll();

                outputContextMenu.Items.Add(copyMenuItem);
                outputContextMenu.Items.Add(selectAllMenuItem);
                outputContextMenu.Opening += (_, __) => isOutputContextMenuOpen = true;
                outputContextMenu.Closed += (_, __) =>
                {
                    isOutputContextMenuOpen = false;
                    TryCompletePendingAutoClose();
                };

                outputTextBox.ContextMenuStrip = outputContextMenu;

                iconPreviewBox = new RoundedPictureBox();
                iconPreviewBox.Width = 72;
                iconPreviewBox.Height = 72;
                iconPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
                iconPreviewBox.Margin = new Padding(0);

                iconSizeLabel = new Label();
                iconSizeLabel.Text = string.Empty;
                iconSizeLabel.TextAlign = ContentAlignment.MiddleCenter;
                iconSizeLabel.AutoSize = false;
                iconSizeLabel.Height = 20;
                iconSizeLabel.Dock = DockStyle.Fill;
                iconSizeLabel.Margin = new Padding(0);

                TableLayoutPanel contentPanel = new TableLayoutPanel();
                contentPanel.Dock = DockStyle.Fill;
                contentPanel.Padding = new Padding(8, 8, 8, 0);
                contentPanel.ColumnCount = 2;
                contentPanel.RowCount = 1;
                contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
                contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                contentPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

                // Do not dock the preview so it can be centered within the left column
                iconPreviewBox.Dock = DockStyle.None;
                iconPreviewBox.Anchor = AnchorStyles.None;
                outputTextBox.Dock = DockStyle.Fill;

                TableLayoutPanel leftPanel = new TableLayoutPanel();
                leftPanel.RowCount = 3;
                leftPanel.ColumnCount = 1;
                leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
                leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
                leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                leftPanel.Dock = DockStyle.Fill;
                leftPanel.Controls.Add(iconPreviewBox, 0, 0);
                leftPanel.Controls.Add(iconSizeLabel, 0, 1);
                iconPreviewBox.Anchor = AnchorStyles.None;
                iconSizeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                contentPanel.Controls.Add(leftPanel, 0, 0);
                contentPanel.Controls.Add(outputTextBox, 1, 0);

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

                retryButton = new Button();
                retryButton.Text = "Retry";
                retryButton.Width = 100;
                retryButton.Visible = false;
                retryButton.Enabled = false;
                retryButton.Click += OnRetryClick;

                buttonPanel.Controls.Add(closeButton);
                buttonPanel.Controls.Add(retryButton);
                buttonPanel.Controls.Add(cancelButton);

                closeCountdownTimer = new System.Windows.Forms.Timer();
                closeCountdownTimer.Interval = 1000;
                closeCountdownTimer.Tick += OnCloseCountdownTick;
                isInProgress = true;

                Controls.Add(contentPanel);
                Controls.Add(buttonPanel);

                WireAutoCloseCancellationOnClick(this);
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
                Size? newSize = null;
                using (MemoryStream ms = new MemoryStream(pngBytes))
                using (Image source = Image.FromStream(ms))
                {
                    iconPreviewBox.Image = new Bitmap(source);
                    newSize = source?.Size;
                }

                if (previous != null)
                {
                    previous.Dispose();
                }

                // Update size label
                if (newSize.HasValue)
                {
                    iconSizeLabel.Text = newSize.Value.Width + " x " + newSize.Value.Height;
                }
                else
                {
                    iconSizeLabel.Text = string.Empty;
                }
            }

            public void AttachCancelAction(Action action)
            {
                cancelAction = action;
            }

            public void AttachRetryAction(Action action)
            {
                retryAction = action;
            }

            public void BeginProgress()
            {
                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action(BeginProgress));
                    return;
                }

                closeCountdownTimer.Stop();
                outputTextBox.Clear();
                ClearPreviewImage();
                pendingAutoClose = false;
                isOutputContextMenuOpen = false;
                cancelButton.Enabled = true;
                closeButton.Text = "OK";
                closeButton.Enabled = false;
                retryButton.Visible = false;
                retryButton.Enabled = false;
                isInProgress = true;
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
                isInProgress = false;

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
                SetTerminalWithoutAutoClose(true);
            }

            public void MarkCanceled()
            {
                SetTerminalWithoutAutoClose(false);
            }

            private void SetTerminalWithoutAutoClose(bool showRetry)
            {
                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action<bool>(SetTerminalWithoutAutoClose), showRetry);
                    return;
                }

                closeCountdownTimer.Stop();
                cancelButton.Enabled = false;
                closeButton.Text = "OK";
                closeButton.Enabled = true;
                retryButton.Visible = showRetry;
                retryButton.Enabled = showRetry;
                isInProgress = false;
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

            private void OnRetryClick(object sender, EventArgs e)
            {
                retryButton.Enabled = false;
                Action action = retryAction;
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
                    if (isOutputContextMenuOpen)
                    {
                        pendingAutoClose = true;
                        closeButton.Text = "OK";
                        return;
                    }

                    Close();
                    return;
                }

                UpdateCloseButtonCountdownText();
            }

            private void OnAnyControlMouseDown(object sender, MouseEventArgs e)
            {
                CancelAutoCloseCountdown();
            }

            private void CancelAutoCloseCountdown()
            {
                if (IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action(CancelAutoCloseCountdown));
                    return;
                }

                if (!closeCountdownTimer.Enabled && !pendingAutoClose && countdownSeconds <= 0)
                {
                    return;
                }

                closeCountdownTimer.Stop();
                pendingAutoClose = false;
                countdownSeconds = 0;
                UpdateCloseButtonCountdownText();
            }

            private void UpdateCloseButtonCountdownText()
            {
                closeButton.Text = countdownSeconds > 0
                    ? "OK (" + countdownSeconds + ")"
                    : "OK";
            }

            private void TryCompletePendingAutoClose()
            {
                if (!pendingAutoClose || IsDisposed)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    BeginInvoke(new Action(TryCompletePendingAutoClose));
                    return;
                }

                if (isOutputContextMenuOpen)
                {
                    return;
                }

                pendingAutoClose = false;
                Close();
            }

            private void WireAutoCloseCancellationOnClick(Control root)
            {
                if (root == null)
                {
                    return;
                }

                root.MouseDown += OnAnyControlMouseDown;
                foreach (Control child in root.Controls)
                {
                    WireAutoCloseCancellationOnClick(child);
                }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape)
                {
                    if (cancelButton.Enabled)
                    {
                        OnCancelClick(this, EventArgs.Empty);
                    }
                    else if (closeButton.Enabled)
                    {
                        Close();
                    }

                    return true;
                }

                return base.ProcessCmdKey(ref msg, keyData);
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing && isInProgress)
                {
                    if (cancelButton.Enabled)
                    {
                        OnCancelClick(this, EventArgs.Empty);
                    }

                    e.Cancel = true;
                    return;
                }

                base.OnFormClosing(e);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                closeCountdownTimer.Stop();
                ClearPreviewImage();

                base.OnFormClosed(e);
            }

            private void ClearPreviewImage()
            {
                Image image = iconPreviewBox.Image;
                if (image != null)
                {
                    iconPreviewBox.Image = null;
                    image.Dispose();
                }

                // Clear size label as well
                try
                {
                    if (iconSizeLabel != null)
                    {
                        iconSizeLabel.Text = string.Empty;
                    }
                }
                catch
                {
                    // ignore UI clear errors
                }
            }
        }

        private sealed class RoundedPictureBox : PictureBox
        {
            private const int CornerRadius = 8;

            protected override void OnPaint(PaintEventArgs pe)
            {
                Graphics graphics = pe.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (GraphicsPath path = CreateRoundedRectPath(ClientRectangle, CornerRadius))
                {
                    Region = new Region(path);

                    using (SolidBrush backBrush = new SolidBrush(BackColor))
                    {
                        graphics.FillPath(backBrush, path);
                    }

                    if (Image != null)
                    {
                        Region previousClip = graphics.Clip;
                        graphics.SetClip(path);
                        graphics.DrawImage(Image, ClientRectangle);
                        graphics.Clip = previousClip;
                    }

                    using (Pen borderPen = new Pen(Color.Gray, 1f))
                    {
                        graphics.DrawPath(borderPen, path);
                    }
                }
            }

            private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
            {
                int diameter = Math.Max(1, radius * 2);
                Rectangle arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
                GraphicsPath path = new GraphicsPath();

                path.AddArc(arc, 180, 90);
                arc.X = rect.Right - diameter;
                path.AddArc(arc, 270, 90);
                arc.Y = rect.Bottom - diameter;
                path.AddArc(arc, 0, 90);
                arc.X = rect.X;
                path.AddArc(arc, 90, 90);
                path.CloseFigure();

                return path;
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
