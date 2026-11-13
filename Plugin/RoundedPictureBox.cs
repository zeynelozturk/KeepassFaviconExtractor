using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FaviconExtractor
{
    internal sealed class RoundedPictureBox : PictureBox
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

}
