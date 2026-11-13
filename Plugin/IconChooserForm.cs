using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FaviconExtractor
{
    internal sealed class IconChooserForm : Form
    {
        private readonly ListView candidates;
        private readonly ImageList thumbnails;
        private readonly PictureBox currentPreview;
        private readonly PictureBox selectedPreview;
        private readonly Label details;
        private readonly Label status;
        private readonly Button useButton;
        private readonly Button cancelButton;
        private readonly string currentKey;
        private readonly List<PreparedIcon> results = new List<PreparedIcon>();
        private readonly List<Image> thumbnailImages = new List<Image>();
        private readonly TaskCompletionSource<PreparedIcon> completion = new TaskCompletionSource<PreparedIcon>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool applying;

        public Task<PreparedIcon> Completion { get { return completion.Task; } }

        // The form owns all preview images; results contain only bytes and metadata.
        public IconChooserForm(string title, Image currentImage, string currentContentKey)
        {
            currentKey = currentContentKey;
            Text = "Choose another icon";
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(760, 490);
            MinimumSize = new Size(650, 530);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = new Label { Text = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title, Dock = DockStyle.Fill, AutoEllipsis = true };
            layout.Controls.Add(heading, 0, 0);
            layout.SetColumnSpan(heading, 2);

            thumbnails = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
            candidates = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false,
                HideSelection = false, LargeImageList = thumbnails, ShowItemToolTips = true, TabIndex = 0 };
            candidates.SelectedIndexChanged += (_, __) => UpdateSelection();
            layout.Controls.Add(candidates, 0, 1);

            var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10, 0, 0, 0) };
            previews.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            previews.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            previews.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            previews.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previews.Controls.Add(new Label { Text = "Current icon", AutoSize = true }, 0, 0);
            currentPreview = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Image = currentImage };
            previews.Controls.Add(currentPreview, 0, 1);
            previews.Controls.Add(new Label { Text = "Selected icon", AutoSize = true }, 0, 2);
            selectedPreview = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(128, 128) };
            previews.Controls.Add(selectedPreview, 0, 3);
            details = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
            previews.Controls.Add(details, 0, 4);
            layout.Controls.Add(previews, 1, 1);
            status = new Label { Text = "Searching for icons…", Dock = DockStyle.Fill, AutoEllipsis = true };
            layout.Controls.Add(status, 0, 2);
            layout.SetColumnSpan(status, 2);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            cancelButton = new Button { Text = "Cancel", AutoSize = true, TabIndex = 2 };
            cancelButton.Click += (_, __) => Close();
            useButton = new Button { Text = "&Use selected icon", AutoSize = true, Enabled = false, TabIndex = 1 };
            useButton.Click += (_, __) =>
            {
                PreparedIcon selected = GetSelected();
                if (selected == null || selected.ContentKey == currentKey) return;
                applying = true;
                useButton.Enabled = false;
                cancelButton.Enabled = false;
                candidates.Enabled = false;
                status.Text = "Applying selected icon…";
                completion.TrySetResult(selected);
            };
            // Deliberately no AcceptButton: Enter while discovery is loading cannot apply an icon.
            CancelButton = cancelButton;
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(useButton);
            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);
        }

        public void AddResult(PreparedIcon icon)
        {
            if (IsDisposed || applying) return;
            results.Add(icon);
            using (var stream = new MemoryStream(icon.PngBytes))
            using (var source = Image.FromStream(stream))
            {
                // ImageList may defer copying images until its native handle is created.
                // Retain originals for the full lifetime of the form.
                var preview = new Bitmap(source);
                thumbnailImages.Add(preview);
                thumbnails.Images.Add(preview);
            }
            bool inUse = icon.ContentKey == currentKey;
            var item = new ListViewItem((inUse ? "Already in use · " : "") + icon.Candidate.Source, results.Count - 1);
            item.ToolTipText = icon.Candidate.IconUri.AbsoluteUri;
            candidates.Items.Add(item);
            // Adding a result must never change selection or keyboard focus.
        }

        public void SetStatus(string text)
        {
            if (!IsDisposed && !applying) status.Text = text;
        }

        public void CompleteSearch(AlternativeIconSearchResult result)
        {
            if (IsDisposed || applying) return;
            bool hasAlternative = results.Exists(r => r.ContentKey != currentKey);
            status.Text = hasAlternative
                ? results.Count + " distinct icons found. Select one, then choose Use selected icon."
                : "No distinct alternative was found. The current icon has been kept.";
            if (result.TimedOut) status.Text += " Search time limit reached; available results can still be used.";
            else if (result.FailedSources > 0) status.Text += " Some sources could not be loaded.";
        }

        private PreparedIcon GetSelected()
        {
            return candidates.SelectedIndices.Count == 1 ? results[candidates.SelectedIndices[0]] : null;
        }

        private void UpdateSelection()
        {
            var selected = GetSelected();
            Image old = selectedPreview.Image;
            selectedPreview.Image = null;
            if (old != null) old.Dispose();
            useButton.Enabled = selected != null && selected.ContentKey != currentKey && !applying;
            details.Text = "";
            if (selected == null) return;
            using (var stream = new MemoryStream(selected.PngBytes))
            using (var source = Image.FromStream(stream))
            {
                selectedPreview.Image = new Bitmap(source);
                details.Text = selected.Candidate.Source + Environment.NewLine +
                    (selected.DecodedSize.HasValue ? "Decoded source: " + selected.DecodedSize.Value.Width + " × " + selected.DecodedSize.Value.Height : "Source: vector / native size unavailable") +
                    Environment.NewLine + "Output: " + source.Width + " × " + source.Height +
                    (selected.ContentKey == currentKey ? Environment.NewLine + "Already in use" : "");
            }
        }

        // Lock/close and termination must be able to close even during an apply.
        public void Abort() { applying = false; Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (applying && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            completion.TrySetResult(null);
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (currentPreview.Image != null) { currentPreview.Image.Dispose(); currentPreview.Image = null; }
                if (selectedPreview.Image != null) { selectedPreview.Image.Dispose(); selectedPreview.Image = null; }
                thumbnails.Dispose();
                foreach (Image image in thumbnailImages) image.Dispose();
                thumbnailImages.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
