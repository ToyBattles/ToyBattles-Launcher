using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Launcher
{
    public class TextProgressBar : ProgressBar
    {
        private readonly SolidBrush backgroundBrush = new SolidBrush(Color.Gray);
        private readonly SolidBrush progressBrush = new SolidBrush(Color.FromArgb(241, 194, 1));
        private readonly SolidBrush textBrush = new SolidBrush(Color.White);
        private readonly StringFormat textFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        public TextProgressBar() : base()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9f);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Refresh();
            base.OnTextChanged(e);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(Text)) Refresh();
            base.OnFontChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.FillRectangle(backgroundBrush, 0, 0, Width, Height);
            double percentage = Maximum == Minimum ? 0 : ((double)(Value - Minimum)) / ((double)(Maximum - Minimum));
            e.Graphics.FillRectangle(progressBrush, new Rectangle(0, 0, (int)(Width * percentage), Height));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                backgroundBrush?.Dispose();
                progressBrush?.Dispose();
                textBrush?.Dispose();
                textFormat?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}