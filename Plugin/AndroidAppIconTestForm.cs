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
            txtPackage.Text = packageName ?? string.Empty;
            txtSource.Text = sourceUrl ?? string.Empty;
            if (icon != null)
            {
                picIcon.Image = new Bitmap(icon);
                lblError.Text = string.Empty;
            }
            else
            {
                picIcon.Image = null;
                lblError.Text = error ?? "No icon retrieved.";
            }
        }
    }
}
