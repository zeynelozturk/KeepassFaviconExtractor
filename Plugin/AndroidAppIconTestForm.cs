using System;
using System.Drawing;
using System.Windows.Forms;

namespace FaviconExtractor
{
    internal partial class AndroidAppIconTestForm : Form
    {
        public AndroidAppIconTestForm()
        {
            InitializeComponent();
        }

        public void SetResult(string packageName, string sourceUrl, Image icon, string error)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetResult(packageName, sourceUrl, icon, error)));
                return;
            }

            txtPackage.Text = packageName ?? string.Empty;
            txtSource.Text = sourceUrl ?? string.Empty;
            if (icon != null)
            {
                Image existing = picIcon.Image;
                if (existing != null && !ReferenceEquals(existing, icon))
                {
                    picIcon.Image = null;
                    existing.Dispose();
                }

                picIcon.Image = new Bitmap(icon);
                lblError.Text = string.Empty;
            }
            else
            {
                if (picIcon.Image != null)
                {
                    var oldImage = picIcon.Image;
                    picIcon.Image = null;
                    oldImage.Dispose();
                }

                lblError.Text = error ?? "No icon retrieved.";
            }
        }

    }
}
