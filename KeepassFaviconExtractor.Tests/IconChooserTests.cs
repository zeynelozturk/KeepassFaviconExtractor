using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FaviconExtractor
{
    [TestClass]
    public class IconChooserTests
    {
        private static FaviconCandidate Candidate(int index)
        {
            return new FaviconCandidate { Source = "test", IconUri = new Uri("https://example.test/" + index + ".png"), TypeAttribute = "image/png" };
        }

        private static byte[] Png(int width = 32, int height = 32)
        {
            using (var bitmap = new Bitmap(width, height))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var output = new MemoryStream())
            {
                graphics.Clear(Color.Teal);
                bitmap.Save(output, ImageFormat.Png);
                return output.ToArray();
            }
        }

        private static AlternativeIconSources Sources()
        {
            return new AlternativeIconSources
            {
                Html = (url, ct) => Task.FromResult(new HtmlFaviconDiscoveryResult { Candidates = new List<FaviconCandidate> { Candidate(0) } }),
                External = (uri, ct) => Task.FromResult(new List<FaviconCandidate> { Candidate(1) }),
                WellKnown = (uris, limit, ct) => Task.FromResult(new List<FaviconCandidate> { Candidate(2) }),
                Logo = (uri, ct) => Task.FromResult(Candidate(3)),
                Prepare = (candidate, ct) => Task.FromResult(new PreparedIcon(candidate, new[] { (byte)candidate.IconUri.AbsolutePath[1] }, null))
            };
        }

        [TestMethod]
        public void Preparation_PreservesAutomaticOutputAndReportsDecodedSize()
        {
            byte[] input = Png(256, 192);
            var candidate = Candidate(0);
            var result = CandidatePreparation.Normalize(candidate, input, CancellationToken.None);
            CollectionAssert.AreEqual(IconNormalizer.NormalizeToPng(input, candidate.TypeAttribute, candidate.IconUri.AbsoluteUri), result.PngBytes);
            Assert.AreEqual(new Size(256, 192), result.DecodedSize.Value);
            using (var stream = new MemoryStream(result.PngBytes))
            using (var image = Image.FromStream(stream)) Assert.AreEqual(new Size(128, 128), image.Size);
        }

        [TestMethod]
        public async Task Search_VisitsEveryExistingSourceEvenWithGoodHtmlIcon()
        {
            var received = new List<PreparedIcon>();
            var result = await AlternativeIconSearch.SearchAsync("https://example.test", false, received.Add, _ => { }, CancellationToken.None, Sources());
            Assert.AreEqual(4, result.Count);
            Assert.AreEqual(4, received.Count);
        }

        [TestMethod]
        public async Task Search_DeduplicatesNormalizedContentAcrossProviders()
        {
            var sources = Sources();
            sources.Prepare = (candidate, ct) => Task.FromResult(new PreparedIcon(candidate, Png(), new Size(32, 32)));
            var received = new List<PreparedIcon>();
            var result = await AlternativeIconSearch.SearchAsync("https://example.test", false, received.Add, _ => { }, CancellationToken.None, sources);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, received.Count);
        }

        [TestMethod]
        public async Task Search_SourceFailureLeavesPartialResultsUsable()
        {
            var sources = Sources();
            sources.External = (uri, ct) => { throw new IOException("offline"); };
            var received = new List<PreparedIcon>();
            var result = await AlternativeIconSearch.SearchAsync("https://example.test", false, received.Add, _ => { }, CancellationToken.None, sources);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(1, result.FailedSources);
        }

        [TestMethod]
        public async Task Search_SourceTimeoutContinuesToOtherSources()
        {
            var sources = Sources();
            sources.SourceTimeout = TimeSpan.FromMilliseconds(30);
            sources.Html = async (url, ct) => { await Task.Delay(10000, ct); return null; };
            var result = await AlternativeIconSearch.SearchAsync("https://example.test", false, _ => { }, _ => { }, CancellationToken.None, sources);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(1, result.FailedSources);
        }

        [TestMethod]
        public async Task Search_CancellationStopsBeforeLaterProviders()
        {
            var sources = Sources();
            sources.External = (uri, ct) => { Assert.Fail("Must not run after cancellation"); return null; };
            using (var cts = new CancellationTokenSource())
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => AlternativeIconSearch.SearchAsync(
                    "https://example.test", false, _ => cts.Cancel(), _ => { }, cts.Token, sources));
        }

        [TestMethod]
        public async Task Search_LargeListsAreBoundedAndUrlDuplicatesAreSkipped()
        {
            var sources = Sources();
            sources.WellKnown = (uris, limit, ct) => Task.FromResult(Enumerable.Range(0, 100).Select(Candidate).ToList());
            int prepared = 0;
            sources.Prepare = (candidate, ct) => { prepared++; return Task.FromResult(new PreparedIcon(candidate, new[] { (byte)prepared }, null)); };
            var result = await AlternativeIconSearch.SearchAsync("https://example.test", false, _ => { }, _ => { }, CancellationToken.None, sources);
            Assert.AreEqual(AlternativeIconSearch.MaxCandidates, prepared);
            Assert.AreEqual(prepared, result.Count);
        }

        [TestMethod]
        public async Task Search_DirectImageDoesNotContactWebsiteProviders()
        {
            var sources = Sources();
            sources.Html = (url, ct) => { Assert.Fail("Direct images should bypass HTML"); return null; };
            var result = await AlternativeIconSearch.SearchAsync("https://example.test/0.png", true, _ => { }, _ => { }, CancellationToken.None, sources);
            Assert.AreEqual(1, result.Count);
        }

        private static PwDatabase Database(out PwEntry entry)
        {
            var db = new PwDatabase();
            db.New(new IOConnectionInfo(), new CompositeKey());
            entry = new PwEntry(true, true);
            db.RootGroup.AddEntry(entry, true);
            return db;
        }

        [TestMethod]
        public void Target_BrowsingDoesNotMutateEntryOrDatabase()
        {
            PwEntry entry;
            var db = Database(out entry);
            db.Modified = false;
            var target = new IconChoiceTarget(db, entry);
            CandidatePreparation.Normalize(Candidate(0), Png(), CancellationToken.None);
            target.Validate();
            Assert.IsTrue(entry.CustomIconUuid.IsZero);
            Assert.AreEqual(0, db.CustomIcons.Count);
            Assert.IsFalse(db.Modified);
            db.Close();
        }

        [TestMethod]
        public void Target_RejectsConcurrentIconChangeAndEntryRemoval()
        {
            PwEntry entry;
            var db = Database(out entry);
            var target = new IconChoiceTarget(db, entry);
            var oldId = entry.IconId;
            entry.IconId = PwIcon.World;
            Assert.ThrowsException<InvalidOperationException>(() => target.Validate());
            entry.IconId = oldId;
            target.Validate();
            db.RootGroup.Entries.Remove(entry);
            Assert.ThrowsException<InvalidOperationException>(() => target.Validate());
            db.Close();
        }

        [TestMethod]
        public void Target_RejectsClosedDatabaseAndReplacedCustomImage()
        {
            PwEntry entry;
            var db = Database(out entry);
            var id = KeePassIconAssigner.AssignNormalizedPngToEntry(db, entry, Png());
            var target = new IconChoiceTarget(db, entry);
            db.CustomIcons.Clear();
            db.CustomIcons.Add(new PwCustomIcon(id, Png(64, 64)));
            Assert.ThrowsException<InvalidOperationException>(() => target.Validate());
            db.Close();
            Assert.ThrowsException<InvalidOperationException>(() => target.Validate());
        }

        [TestMethod]
        public void Target_ApplyUsesCapturedEntryAndReusesStoredIcon()
        {
            PwEntry entry;
            var db = Database(out entry);
            var other = new PwEntry(true, true);
            db.RootGroup.AddEntry(other, true);
            byte[] bytes = Png();
            var stored = KeePassIconAssigner.AssignNormalizedPngToEntry(db, other, bytes);
            var target = new IconChoiceTarget(db, entry);
            target.Validate();
            KeePassIconAssigner.AssignNormalizedPngToEntry(target.Database, target.Entry, bytes);
            Assert.AreEqual(stored, entry.CustomIconUuid);
            Assert.AreEqual(stored, other.CustomIconUuid);
            Assert.AreEqual(1, db.CustomIcons.Count);
            db.Close();
        }

        private static void Sta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(10000), "UI test did not finish");
            if (error != null) throw error;
        }

        [TestMethod]
        public void Target_RejectsEntryMovedToRecycleBin()
        {
            PwEntry entry;
            var db = Database(out entry);
            var target = new IconChoiceTarget(db, entry);
            var recycle = new PwGroup(true, true);
            db.RootGroup.AddGroup(recycle, true);
            db.RecycleBinUuid = recycle.Uuid;
            db.RootGroup.Entries.Remove(entry);
            recycle.AddEntry(entry, true);
            Assert.ThrowsException<InvalidOperationException>(() => target.Validate());
            db.Close();
        }

        [TestMethod]
        public void Chooser_LayoutFitsAtEnlargedScaling()
        {
            Sta(() =>
            {
                foreach (float scale in new[] { 1.25f, 1.5f })
                using (var form = new IconChooserForm("Example website", new Bitmap(64, 64), null))
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-3000, -3000);
                    for (int i = 0; i < 8; i++)
                        form.AddResult(new PreparedIcon(Candidate(i), Png(), new Size(256, 256)));
                    form.CompleteSearch(new AlternativeIconSearchResult { Count = 8 });
                    form.Scale(new SizeF(scale, scale));
                    form.Show();
                    Application.DoEvents();
                    var list = Field<ListView>(form, "candidates");
                    Assert.AreEqual(0, list.SelectedIndices.Count);
                    var button = Field<Button>(form, "useButton");
                    Assert.IsFalse(button.Enabled);
                    Assert.IsTrue(button.Parent.ClientRectangle.Contains(button.Bounds),
                        "Apply button must fit inside its panel: " + button.Bounds + " within " + button.Parent.ClientRectangle);
                    var details = Field<Label>(form, "details");
                    details.Text = "html-link\nDecoded source: 256 × 256\nOutput: 128 × 128\nAlready in use";
                    Assert.IsTrue(details.Height >= details.PreferredHeight, "Decoded and output dimensions must fit");
                    string qaDirectory = Environment.GetEnvironmentVariable("FAVICON_CHOOSER_QA_DIR");
                    if (!string.IsNullOrEmpty(qaDirectory))
                    {
                        Directory.CreateDirectory(qaDirectory);
                        using (var capture = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(capture, new Rectangle(0, 0, form.Width, form.Height));
                            capture.Save(Path.Combine(qaDirectory, "chooser-" + (int)(scale * 100) + ".png"), ImageFormat.Png);
                        }
                    }
                    form.Abort();
                }
            });
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        [TestMethod]
        public void Chooser_LoadingDoesNotPreselectOrAllowEnterToApply()
        {
            Sta(() =>
            {
                using (var form = new IconChooserForm("Test entry", null, null))
                {
                    form.AddResult(new PreparedIcon(Candidate(0), Png(), new Size(32, 32)));
                    Assert.AreNotEqual(IntPtr.Zero, Field<ImageList>(form, "thumbnails").Handle,
                        "Thumbnails must remain valid when the image list creates its native handle later");
                    Assert.AreEqual(0, Field<ListView>(form, "candidates").SelectedIndices.Count);
                    Assert.IsFalse(Field<Button>(form, "useButton").Enabled);
                    Assert.IsNull(form.AcceptButton);
                    Assert.IsFalse(form.Completion.IsCompleted);
                }
            });
        }

        [TestMethod]
        public void StatusShortcut_StopsCountdownBeforeInvokingChooser()
        {
            Sta(() =>
            {
                var type = typeof(FaviconExtractorExt).GetNestedType("ExtractionStatusForm", BindingFlags.NonPublic);
                using (var form = (Form)Activator.CreateInstance(type, true))
                {
                    bool invoked = false;
                    Action action = () =>
                    {
                        Assert.IsFalse(Field<System.Windows.Forms.Timer>(form, "closeCountdownTimer").Enabled);
                        invoked = true;
                    };
                    type.GetMethod("AttachChooseAction").Invoke(form, new object[] { action });
                    type.GetMethod("MarkCompletedWithCountdown").Invoke(form, new object[] { 3 });
                    Assert.IsTrue(Field<System.Windows.Forms.Timer>(form, "closeCountdownTimer").Enabled);
                    typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Field<Button>(form, "chooseButton"), new object[] { EventArgs.Empty });
                    Assert.IsTrue(invoked);
                }
            });
        }
    }
}
