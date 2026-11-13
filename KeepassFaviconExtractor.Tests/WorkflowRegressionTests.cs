using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePass.Util;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FaviconExtractor
{
    [TestClass]
    public class WorkflowRegressionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
        }

        private static object Call(object instance, string name, params object[] args)
        {
            return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(instance, args);
        }

        private static void PumpUntil(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(8), "Workflow did not finish within the test deadline");
                Application.DoEvents();
                Thread.Sleep(1);
            }
            Application.DoEvents();
        }

        private static void Finish(Task task)
        {
            PumpUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        private static void Sta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(15000), "Workflow test thread did not finish");
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static byte[] Png(Color color)
        {
            using (var bitmap = new Bitmap(64, 64))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var stream = new MemoryStream())
            {
                graphics.Clear(color);
                bitmap.Save(stream, ImageFormat.Png);
                return IconNormalizer.NormalizeToPng(stream.ToArray(), "image/png", "https://example.test/icon.png");
            }
        }

        private static FaviconCandidate Candidate(string name, string source = "test", int score = 100, int size = 64)
        {
            return new FaviconCandidate { Source = source, IconUri = new Uri("https://example.test/" + name + ".png"),
                TypeAttribute = "image/png", BestSize = new Size(size, size), Score = score };
        }

        private static HtmlFaviconDiscoveryResult Discovery(params FaviconCandidate[] candidates)
        {
            return new HtmlFaviconDiscoveryResult { PageUri = new Uri("https://example.test"),
                Candidates = candidates.ToList(), BestCandidate = candidates.FirstOrDefault() };
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly MainForm Window;
            internal readonly PwDatabase Database;
            internal readonly FaviconWorkflowServices Services;
            internal readonly FaviconExtractorExt Plugin;
            internal readonly List<PwEntry> Refreshed = new List<PwEntry>();

            internal Fixture()
            {
                Window = new MainForm { ShowInTaskbar = false, Opacity = 0 };
                Database = Window.DocumentManager.ActiveDatabase;
                Database.New(new IOConnectionInfo(), new CompositeKey());
                Services = new FaviconWorkflowServices
                {
                    Discover = (url, token, status) => Task.FromResult(Discovery(Candidate("default"))),
                    Download = (uri, token, strict) => Task.FromResult(Png(Color.Green)),
                    DiscoverRescue = (uri, token) => Task.FromResult(new List<FaviconCandidate>()),
                    SearchAlternatives = (url, direct, result, status, token) => Task.FromResult(new AlternativeIconSearchResult()),
                    RefreshIcons = entry => Refreshed.Add(entry)
                };
                Plugin = new FaviconExtractorExt(Services);
                // Use KeePass 2.61's real host implementation with an isolated, unshown MainForm.
                var hostType = typeof(IPluginHost).Assembly.GetType("KeePass.Plugins.DefaultPluginHost", true);
                var host = (IPluginHost)Activator.CreateInstance(hostType, true);
                hostType.GetMethod("Initialize").Invoke(host, new object[] { Window, new CommandLineArgs(new string[0]), CipherPool.GlobalPool });
                Assert.IsTrue(Plugin.Initialize(host));
            }

            internal PwEntry Entry(string name, string url = "https://example.test")
            {
                var entry = new PwEntry(true, true);
                entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, name));
                entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, url));
                Database.RootGroup.AddEntry(entry, true);
                return entry;
            }

            internal void Select(params PwEntry[] entries)
            {
                var list = Field<ListView>(Window, "m_lvEntries");
                list.Items.Clear();
                var handle = list.Handle;
                foreach (var entry in entries)
                {
                    var item = new ListViewItem(entry.Strings.ReadSafe(PwDefs.TitleField)) { Tag = new PwListItem(entry) };
                    list.Items.Add(item);
                    item.Selected = true;
                }
                Assert.AreEqual(entries.Length, Window.GetSelectedEntries().Length);
            }

            internal Task Choose(PwEntry entry)
            {
                return (Task)Call(Plugin, "RunIconChooserAsync", Database, entry, entry.Strings.ReadSafe(PwDefs.UrlField));
            }

            internal Form StartExtraction()
            {
                Call(Plugin, "OnExtractFaviconClick", null, EventArgs.Empty);
                return Field<Form>(Plugin, "activeExtractionStatusForm");
            }

            internal void AssertIdle()
            {
                Assert.IsFalse(Field<bool>(Plugin, "isExtractRunning"));
                Assert.IsNull(Field<CancellationTokenSource>(Plugin, "extractCancellationTokenSource"));
            }

            public void Dispose()
            {
                Plugin.Terminate();
                foreach (Form owned in Window.OwnedForms) owned.Dispose();
                Database.Close();
                Window.Dispose();
            }
        }

        [TestMethod]
        public void Chooser_ApplyCancelsSearchReusesIconAndChangesOnlyCapturedEntry()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target");
                    var other = f.Entry("other");
                    var old = KeePassIconAssigner.AssignNormalizedPngToEntry(f.Database, target, Png(Color.Red));
                    byte[] replacement = Png(Color.Blue);
                    var stored = KeePassIconAssigner.AssignNormalizedPngToEntry(f.Database, other, replacement);
                    bool canceled = false;
                    f.Services.SearchAlternatives = async (url, direct, result, status, token) =>
                    {
                        result(new PreparedIcon(Candidate("replacement"), replacement, new Size(64, 64)));
                        try { await Task.Delay(Timeout.Infinite, token); }
                        catch (OperationCanceledException) { canceled = true; throw; }
                        return new AlternativeIconSearchResult();
                    };
                    Task operation = f.Choose(target);
                    var form = Field<IconChooserForm>(f.Plugin, "activeIconChooser");
                    var list = Field<ListView>(form, "candidates");
                    PumpUntil(() => list.Items.Count == 1);
                    f.Select(other); // Changing KeePass selection must not redirect the pending choice.
                    list.Items[0].Selected = true;
                    Application.DoEvents();
                    Assert.AreEqual(old, target.CustomIconUuid, "Selection alone must not assign");
                    Field<Button>(form, "useButton").PerformClick();
                    Finish(operation);
                    Assert.IsTrue(canceled);
                    Assert.AreEqual(stored, target.CustomIconUuid);
                    Assert.AreEqual(stored, other.CustomIconUuid);
                    Assert.AreEqual(2, f.Database.CustomIcons.Count, "Reuse the replacement and retain the old icon");
                    CollectionAssert.AreEqual(new[] { target }, f.Refreshed.ToArray());
                    Assert.IsTrue(form.IsDisposed);
                    f.AssertIdle();
                }
            });
        }

        [TestMethod]
        public void Chooser_CancelAfterSelectingCandidateDoesNotChangeDatabase()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target");
                    var old = KeePassIconAssigner.AssignNormalizedPngToEntry(f.Database, target, Png(Color.Red));
                    f.Database.Modified = false;
                    f.Services.SearchAlternatives = (url, direct, result, status, token) =>
                    {
                        result(new PreparedIcon(Candidate("replacement"), Png(Color.Blue), null));
                        return Task.FromResult(new AlternativeIconSearchResult { Count = 1 });
                    };
                    Task operation = f.Choose(target);
                    var form = Field<IconChooserForm>(f.Plugin, "activeIconChooser");
                    var list = Field<ListView>(form, "candidates");
                    PumpUntil(() => list.Items.Count == 1);
                    list.Items[0].Selected = true;
                    Application.DoEvents();
                    Assert.IsTrue(Field<Button>(form, "useButton").Enabled);
                    Field<Button>(form, "cancelButton").PerformClick();
                    Finish(operation);
                    Assert.AreEqual(old, target.CustomIconUuid);
                    Assert.AreEqual(1, f.Database.CustomIcons.Count);
                    Assert.IsFalse(f.Database.Modified);
                    Assert.AreEqual(0, f.Refreshed.Count);
                    f.AssertIdle();
                }
            });
        }

        [TestMethod]
        public void Chooser_CloseDuringSearchIgnoresLateResultsAndAllowsAnotherOperation()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target");
                    var old = KeePassIconAssigner.AssignNormalizedPngToEntry(f.Database, target, Png(Color.Red));
                    f.Database.Modified = false;
                    bool started = false, canceled = false;
                    f.Services.SearchAlternatives = async (url, direct, result, status, token) =>
                    {
                        started = true;
                        try { await Task.Delay(Timeout.Infinite, token); }
                        catch (OperationCanceledException)
                        {
                            canceled = true;
                            // Simulate a source that delivers one last callback during cancellation.
                            result(new PreparedIcon(Candidate("late"), Png(Color.Blue), null));
                            status("late status");
                        }
                        return new AlternativeIconSearchResult { Count = 1 };
                    };
                    Task first = f.Choose(target);
                    var form = Field<IconChooserForm>(f.Plugin, "activeIconChooser");
                    PumpUntil(() => started);
                    form.Close();
                    Finish(first);
                    Assert.IsTrue(canceled);
                    Assert.IsTrue(form.IsDisposed);
                    Assert.AreEqual(old, target.CustomIconUuid);
                    Assert.AreEqual(1, f.Database.CustomIcons.Count);
                    Assert.IsFalse(f.Database.Modified);
                    f.AssertIdle();

                    f.Services.SearchAlternatives = (url, direct, result, status, token) => Task.FromResult(new AlternativeIconSearchResult());
                    Task second = f.Choose(target);
                    var reopened = Field<IconChooserForm>(f.Plugin, "activeIconChooser");
                    Assert.IsNotNull(reopened);
                    Field<Button>(reopened, "cancelButton").PerformClick();
                    Finish(second);
                    f.AssertIdle();
                }
            });
        }

        [TestMethod]
        public void Chooser_DatabaseClosingEventCancelsWithoutAssigning()
        {
            ChooserLifecycle(false);
        }

        [TestMethod]
        public void Chooser_PluginTerminationCancelsWithoutAssigning()
        {
            ChooserLifecycle(true);
        }

        private static void ChooserLifecycle(bool terminate)
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target");
                    var old = KeePassIconAssigner.AssignNormalizedPngToEntry(f.Database, target, Png(Color.Red));
                    bool started = false, canceled = false;
                    f.Services.SearchAlternatives = async (url, direct, result, status, token) =>
                    {
                        started = true;
                        try { await Task.Delay(Timeout.Infinite, token); }
                        catch (OperationCanceledException) { canceled = true; throw; }
                        return new AlternativeIconSearchResult();
                    };
                    Task operation = f.Choose(target);
                    PumpUntil(() => started);
                    if (terminate) f.Plugin.Terminate();
                    else
                    {
                        // Deliver the actual KeePass event args; this tests the plugin's event response,
                        // not KeePass's own lock command implementation.
                        var handler = Field<EventHandler<FileClosingEventArgs>>(f.Window, "FileClosingPre");
                        handler(f.Window, new FileClosingEventArgs(f.Database, (FileEventFlags)0));
                    }
                    Finish(operation);
                    Assert.IsTrue(canceled);
                    Assert.AreEqual(old, target.CustomIconUuid);
                    Assert.AreEqual(1, f.Database.CustomIcons.Count);
                    Assert.IsNull(Field<IconChooserForm>(f.Plugin, "activeIconChooser"));
                    f.AssertIdle();
                }
            });
        }

        [TestMethod]
        public void AutomaticExtraction_UsesRankedFallbackAndKeepsThreeSecondCountdown()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target");
                    var calls = new List<string>();
                    f.Services.Discover = (url, token, status) => Task.FromResult(Discovery(Candidate("broken"), Candidate("working")));
                    f.Services.Download = (uri, token, strict) =>
                    {
                        calls.Add(uri.AbsolutePath);
                        return Task.FromResult(uri.AbsolutePath.Contains("broken") ? new byte[] { 0, 1, 2 } : Png(Color.Green));
                    };
                    f.Select(target);
                    using (Form status = f.StartExtraction())
                    {
                        Assert.IsNotNull(status);
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.IsFalse(target.CustomIconUuid.IsZero);
                        CollectionAssert.AreEqual(new[] { "/broken.png", "/working.png" }, calls);
                        Assert.AreEqual(3, Field<int>(status, "countdownSeconds"));
                        Assert.IsTrue(Field<System.Windows.Forms.Timer>(status, "closeCountdownTimer").Enabled);
                        f.AssertIdle();
                    }
                }
            });
        }

        [TestMethod]
        public void AutomaticExtraction_FailedHtmlUsesExternalRescueAndSkipsRepeatedUrls()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target");
                    var broken = Candidate("broken", "html-link");
                    var calls = new List<string>();
                    int rescueCalls = 0;
                    f.Services.Discover = (url, token, status) => Task.FromResult(Discovery(broken));
                    f.Services.DiscoverRescue = (uri, token) =>
                    {
                        rescueCalls++;
                        return Task.FromResult(new List<FaviconCandidate> { broken, Candidate("rescue", "external-test") });
                    };
                    f.Services.Download = (uri, token, strict) =>
                    {
                        calls.Add(uri.AbsolutePath);
                        return Task.FromResult(uri.AbsolutePath.Contains("broken") ? new byte[] { 0 } : Png(Color.Blue));
                    };
                    f.Select(target);
                    using (Form status = f.StartExtraction())
                    {
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.IsFalse(target.CustomIconUuid.IsZero);
                        Assert.AreEqual(1, rescueCalls);
                        CollectionAssert.AreEqual(new[] { "/broken.png", "/rescue.png" }, calls);
                        StringAssert.Contains(Field<TextBox>(status, "outputTextBox").Text, "Assigned from rescue candidate");
                    }
                }
            });
        }

        [TestMethod]
        public void AutomaticExtraction_BatchContinuesAfterFailureAndSkipsMissingUrl()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var bad = f.Entry("bad", "https://example.test/bad.png");
                    var good = f.Entry("good", "https://example.test/good.png");
                    var missing = f.Entry("missing", "");
                    f.Services.Download = (uri, token, strict) => Task.FromResult(uri.AbsolutePath.Contains("bad") ? new byte[] { 0 } : Png(Color.Blue));
                    f.Select(bad, good, missing);
                    using (Form status = f.StartExtraction())
                    {
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.IsTrue(bad.CustomIconUuid.IsZero);
                        Assert.IsFalse(good.CustomIconUuid.IsZero);
                        Assert.IsTrue(missing.CustomIconUuid.IsZero);
                        Assert.AreEqual(1, f.Database.CustomIcons.Count);
                        StringAssert.Contains(Field<TextBox>(status, "outputTextBox").Text, "1 assigned, 1 failed, 1 skipped");
                        Assert.IsFalse(Field<System.Windows.Forms.Timer>(status, "closeCountdownTimer").Enabled);
                        f.AssertIdle();
                    }
                }
            });
        }

        [TestMethod]
        public void AutomaticExtraction_CancelDuringDownloadPreservesIconAndCanStartAgain()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target", "https://example.test/icon.png");
                    var old = KeePassIconAssigner.AssignNormalizedPngToEntry(f.Database, target, Png(Color.Red));
                    f.Database.Modified = false;
                    bool started = false, canceled = false;
                    f.Services.Download = async (uri, token, strict) =>
                    {
                        started = true;
                        try { await Task.Delay(Timeout.Infinite, token); }
                        catch (OperationCanceledException) { canceled = true; throw; }
                        return Png(Color.Blue);
                    };
                    f.Select(target);
                    using (Form status = f.StartExtraction())
                    {
                        PumpUntil(() => started);
                        Field<Button>(status, "cancelButton").PerformClick();
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.IsTrue(canceled);
                        Assert.AreEqual(old, target.CustomIconUuid);
                        Assert.AreEqual(1, f.Database.CustomIcons.Count);
                        Assert.IsFalse(f.Database.Modified);
                        Assert.AreEqual(0, Field<ProgressBar>(status, "extractionProgressBar").MarqueeAnimationSpeed);
                        f.AssertIdle();
                    }
                    f.Services.Download = (uri, token, strict) => Task.FromResult(Png(Color.Green));
                    using (Form second = f.StartExtraction())
                    {
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.AreNotEqual(old, target.CustomIconUuid);
                        Assert.AreEqual(2, f.Database.CustomIcons.Count);
                        f.AssertIdle();
                    }
                }
            });
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void AutomaticExtraction_PostAssignmentRescueReplacesOrKeepsInitialIcon(bool rescueCanceled)
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target");
                    var calls = new List<string>();
                    byte[] initial = Png(Color.Green), replacement = Png(Color.Blue);
                    f.Services.Discover = (url, token, status) => Task.FromResult(Discovery(
                        Candidate("broken", "html-link", 500), Candidate("tiny", "html-link", 100, 16)));
                    f.Services.DiscoverRescue = (uri, token) => Task.FromResult(new List<FaviconCandidate> { Candidate("rescue", "external-test", 300) });
                    f.Services.Download = (uri, token, strict) =>
                    {
                        calls.Add(uri.AbsolutePath);
                        if (uri.AbsolutePath.Contains("broken")) return Task.FromResult(new byte[] { 0 });
                        if (uri.AbsolutePath.Contains("tiny")) return Task.FromResult(initial);
                        if (rescueCanceled) return Task.FromException<byte[]>(new OperationCanceledException());
                        return Task.FromResult(replacement);
                    };
                    f.Select(target);
                    using (Form status = f.StartExtraction())
                    {
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        CollectionAssert.AreEqual(new[] { "/broken.png", "/tiny.png", "/rescue.png" }, calls);
                        var assigned = f.Database.CustomIcons.Single(i => i.Uuid.Equals(target.CustomIconUuid));
                        CollectionAssert.AreEqual(rescueCanceled ? initial : replacement, assigned.ImageDataPng);
                        Assert.AreEqual(rescueCanceled ? 1 : 2, f.Database.CustomIcons.Count);
                        Assert.IsTrue(Field<System.Windows.Forms.Timer>(status, "closeCountdownTimer").Enabled,
                            "Rescue cancellation must keep the initial assignment as a successful result");
                        if (rescueCanceled) StringAssert.Contains(Field<TextBox>(status, "outputTextBox").Text, "Keeping assigned icon");
                        f.AssertIdle();
                    }
                }
            });
        }

        [TestMethod]
        public void AutomaticExtraction_RetryStartsFreshAndReusesTheStatusWindow()
        {
            Sta(() =>
            {
                using (var f = new Fixture())
                {
                    var target = f.Entry("target", "https://example.test/icon.png");
                    var response = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    int downloads = 0;
                    f.Services.Download = (uri, token, strict) => { downloads++; return response.Task; };
                    f.Select(target);
                    using (Form status = f.StartExtraction())
                    {
                        response.SetResult(new byte[] { 0 });
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.IsTrue(target.CustomIconUuid.IsZero);
                        Assert.IsTrue(Field<Button>(status, "retryButton").Enabled);
                        f.AssertIdle();
                        response = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                        Field<Button>(status, "retryButton").PerformClick();
                        Assert.IsTrue(Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.IsFalse(Field<Button>(status, "chooseButton").Enabled);
                        response.SetResult(Png(Color.Green));
                        PumpUntil(() => !Field<bool>(f.Plugin, "isExtractRunning"));
                        Assert.AreEqual(2, downloads);
                        Assert.IsFalse(target.CustomIconUuid.IsZero);
                        Assert.IsFalse(status.IsDisposed);
                        Assert.IsTrue(Field<Button>(status, "chooseButton").Enabled);
                        f.AssertIdle();
                    }
                }
            });
        }
    }
}
