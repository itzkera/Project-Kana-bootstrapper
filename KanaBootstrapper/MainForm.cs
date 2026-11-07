using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KanaBootstrapper;

internal class MainForm : Form
{
	private const int WindowWidth = 520;

	private const int WindowHeight = 320;

	private const int LogoSize = 96;

	private const int SideMargin = 52;

	private readonly string _protocolArgument;

	private readonly bool _launchMode;

	private readonly Installer _installer;

	private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

	private PictureBox _logo;

	private Label _status;

	private ProgressStrip _bar;

	private Label _close;

	private const int WmNcLButtonDown = 161;

	private const int HtCaption = 2;

	public bool Preview { get; set; }

	public MainForm(string protocolArgument)
	{
		_protocolArgument = protocolArgument;
		_installer = new Installer();
		_launchMode = !string.IsNullOrEmpty(protocolArgument);
		BuildUi();
	}

	private void BuildUi()
	{
		Text = "Kana";
		base.ClientSize = new Size(520, 320);
		base.FormBorderStyle = FormBorderStyle.None;
		base.StartPosition = FormStartPosition.CenterScreen;
		BackColor = Color.White;
		base.ShowInTaskbar = true;
		Font = new Font("Segoe UI", 9f);
		base.Icon = TryLoadIcon();
		base.Paint += delegate(object s, PaintEventArgs e)
		{
			using Pen pen = new Pen(Color.FromArgb(205, 205, 205));
			e.Graphics.DrawRectangle(pen, 0, 0, base.ClientSize.Width - 1, base.ClientSize.Height - 1);
		};
		_logo = new PictureBox
		{
			Size = new Size(96, 96),
			Location = new Point(212, 56),
			SizeMode = PictureBoxSizeMode.Zoom,
			BackColor = Color.Transparent,
			Image = TryLoadLogo()
		};
		base.Controls.Add(_logo);
		_status = new Label
		{
			Text = "Starting Kana ...",
			Font = new Font("Segoe UI", 10f),
			ForeColor = Color.FromArgb(60, 60, 60),
			TextAlign = ContentAlignment.MiddleCenter,
			AutoSize = false,
			Size = new Size(480, 42),
			Location = new Point(20, 182)
		};
		base.Controls.Add(_status);
		_bar = new ProgressStrip
		{
			Size = new Size(416, 16),
			Location = new Point(52, 236)
		};
		base.Controls.Add(_bar);
		_close = new Label
		{
			Text = "✕",
			Font = new Font("Segoe UI", 10f),
			ForeColor = Color.FromArgb(150, 150, 150),
			AutoSize = false,
			Size = new Size(28, 24),
			TextAlign = ContentAlignment.MiddleCenter,
			Location = new Point(488, 6),
			Cursor = Cursors.Hand
		};
		_close.MouseEnter += delegate
		{
			_close.ForeColor = Color.FromArgb(70, 70, 70);
		};
		_close.MouseLeave += delegate
		{
			_close.ForeColor = Color.FromArgb(150, 150, 150);
		};
		_close.Click += delegate
		{
			Close();
		};
		base.Controls.Add(_close);
		base.MouseDown += StartDrag;
		_logo.MouseDown += StartDrag;
		_status.MouseDown += StartDrag;
		base.FormClosing += delegate
		{
			_cancel.Cancel();
		};
		base.Shown += async delegate
		{
			await RunAsync().ConfigureAwait(continueOnCapturedContext: true);
		};
	}

	private async Task RunAsync()
	{
		_ = 6;
		try
		{
			if (Preview)
			{
				await Task.Delay(-1, _cancel.Token).ConfigureAwait(continueOnCapturedContext: true);
				return;
			}
			if (Config.Source == ClientSource.Archive)
			{
				if (!(await _installer.IsArchiveCurrentAsync(_cancel.Token).ConfigureAwait(continueOnCapturedContext: true)))
				{
					InstallOptions options = new InstallOptions
					{
						DesktopShortcut = true,
						RegisterProtocol = true
					};
					await _installer.InstallArchiveAsync(options, new Progress<Status>(delegate(Status s)
					{
						_status.Text = s.Text;
					}), _cancel.Token).ConfigureAwait(continueOnCapturedContext: true);
				}
				else
				{
					Installer.RegisterProtocol();
				}
			}
			else if (Config.Source == ClientSource.Manifest)
			{
				Manifest manifest = await _installer.FetchManifestAsync(_cancel.Token).ConfigureAwait(continueOnCapturedContext: true);
				if (!_installer.IsClientCurrent(manifest.Version))
				{
					InstallOptions options2 = new InstallOptions
					{
						DesktopShortcut = true,
						RegisterProtocol = true
					};
					await _installer.InstallAsync(manifest, options2, null, _cancel.Token).ConfigureAwait(continueOnCapturedContext: true);
				}
				else
				{
					Installer.RegisterProtocol();
				}
			}
			else
			{
				EnsureInstalled();
			}
			if (_launchMode)
			{
				await _installer.LaunchFromProtocolAsync(_protocolArgument, new Progress<Status>(delegate(Status s)
				{
					_status.Text = s.Text;
				}), _cancel.Token).ConfigureAwait(continueOnCapturedContext: true);
			}
			await Task.Delay(_launchMode ? 1200 : 2000, _cancel.Token).ConfigureAwait(continueOnCapturedContext: true);
			Close();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Fail(ex2);
		}
	}

	private void EnsureInstalled()
	{
		if (!File.Exists(_installer.PlayerPath()))
		{
			throw new Exception("The Kana client is not installed yet.");
		}
		Installer.RegisterProtocol();
	}

	private void Fail(Exception ex)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke((Action)delegate
			{
				Fail(ex);
			});
		}
		else
		{
			_status.ForeColor = Color.FromArgb(190, 40, 40);
			_status.Text = ex.Message;
			_bar.Freeze();
		}
	}

	private static Image TryLoadLogo()
	{
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
			if (stream == null)
			{
				return null;
			}
			using MemoryStream memoryStream = new MemoryStream();
			stream.CopyTo(memoryStream);
			memoryStream.Position = 0L;
			return Image.FromStream(memoryStream);
		}
		catch
		{
			return null;
		}
	}

	private static Icon TryLoadIcon()
	{
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
			if (stream == null)
			{
				return null;
			}
			return new Icon(stream, SystemInformation.IconSize);
		}
		catch
		{
			return null;
		}
	}

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

	private void StartDrag(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			ReleaseCapture();
			SendMessage(base.Handle, 161, 2, 0);
		}
	}
}
