using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KanaBootstrapper;

internal class ProgressStrip : Control
{
	private readonly Timer _timer;

	private int _offset;

	public int Step { get; set; } = 4;

	public int BlockWidth { get; set; } = 150;

	public Color TrackColor { get; set; } = Color.FromArgb(230, 230, 230);

	public Color TrackBorderColor { get; set; } = Color.FromArgb(200, 200, 200);

	public Color FillTopColor { get; set; } = ColorTranslator.FromHtml("#06B025");

	public Color FillBottomColor { get; set; } = ColorTranslator.FromHtml("#55C96A");

	public ProgressStrip()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		_timer = new Timer
		{
			Interval = 16
		};
		_timer.Tick += delegate
		{
			_offset += Step;
			if (_offset > base.Width)
			{
				_offset = -BlockWidth;
			}
			Invalidate();
		};
		_offset = -BlockWidth;
	}

	public void Freeze()
	{
		_timer.Stop();
		Invalidate();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		_offset = -BlockWidth;
		_timer.Start();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_timer.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Rectangle rect = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
		using (SolidBrush brush = new SolidBrush(TrackColor))
		{
			graphics.FillRectangle(brush, rect);
		}
		using (Pen pen = new Pen(TrackBorderColor))
		{
			graphics.DrawRectangle(pen, rect);
		}
		if (!_timer.Enabled)
		{
			return;
		}
		int num = ((_offset < 1) ? 1 : _offset);
		int num2 = _offset + BlockWidth;
		if (num2 > base.Width - 1)
		{
			num2 = base.Width - 1;
		}
		if (num2 <= num)
		{
			return;
		}
		Rectangle rect2 = new Rectangle(num, 1, num2 - num, base.Height - 2);
		using LinearGradientBrush brush2 = new LinearGradientBrush(new Rectangle(0, 1, base.Width, base.Height - 2), FillTopColor, FillBottomColor, LinearGradientMode.Vertical);
		graphics.FillRectangle(brush2, rect2);
	}
}
