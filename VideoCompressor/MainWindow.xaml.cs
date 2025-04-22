using System.Diagnostics;
using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;

namespace VideoCompressor
{
	public partial class MainWindow : Window
	{
		// State tracking
		private string _sourceVideoPath = string.Empty;
		private string _compressedOutputPath = string.Empty;
		private Process? _activeCompressionProcess = null;

		public MainWindow()
		{
			InitializeComponent();
			SetupEventHandlers();
		}

		private void SetupEventHandlers()
		{
			// Update quality display when slider moves
			QualitySlider.ValueChanged += (_, _) =>
				QualityValueText.Text = $"CRF: {(int)QualitySlider.Value}";

			// Safe event subscription for window closing
			Closing += (_, e) => HandleWindowClosing(e);
		}

		private void BrowseButton_Click(object sender, RoutedEventArgs e)
		{
			var dialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov"
			};

			if (dialog.ShowDialog() == true)
			{
				_sourceVideoPath = dialog.FileName;
				SelectedFileText.Text = Path.GetFileName(_sourceVideoPath);
			}
		}

		private async void CompressButton_Click(object sender, RoutedEventArgs e)
		{
			if (!ValidateInputFile()) return;

			PrepareOutputPath();
			if (!ConfirmOverwriteIfNeeded()) return;

			await RunCompressionProcess();
		}

		#region Compression Logic
		private bool ValidateInputFile()
		{
			if (!string.IsNullOrEmpty(_sourceVideoPath)) return true;

			MessageBox.Show("Please select a video first!");
			return false;
		}

		private void PrepareOutputPath()
		{
			string? directory = Path.GetDirectoryName(_sourceVideoPath);
			string fileName = Path.GetFileName(_sourceVideoPath);

			_compressedOutputPath = directory == null
				? Path.Combine(Directory.GetCurrentDirectory(), $"compressed_{fileName}")
				: Path.Combine(directory, $"compressed_{fileName}");
		}

		private bool ConfirmOverwriteIfNeeded()
		{
			if (!File.Exists(_compressedOutputPath)) return true;

			var overwrite = MessageBox.Show(
				"Compressed file already exists. Overwrite?",
				"Warning",
				MessageBoxButton.YesNo,
				MessageBoxImage.Warning);

			return overwrite == MessageBoxResult.Yes;
		}

		private async Task RunCompressionProcess()
		{
			SetUiBusyState(true);
			_activeCompressionProcess = CreateFfmpegProcess();

			try
			{
				await ExecuteFfmpeg();
				HandleCompressionResult();
			}
			catch (Exception ex)
			{
				HandleCompressionError(ex);
			}
			finally
			{
				CleanupResources();
			}
		}

		private Process CreateFfmpegProcess()
		{
			int quality = (int)QualitySlider.Value;
			string arguments = $"-i \"{_sourceVideoPath}\" -c:v libx264 -crf {quality} -preset fast \"{_compressedOutputPath}\"";

			return new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = "ffmpeg.exe",
					Arguments = arguments,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
					RedirectStandardOutput = true
				},
				EnableRaisingEvents = true
			};
		}

		private async Task ExecuteFfmpeg()
		{
			_activeCompressionProcess!.Start();
			_activeCompressionProcess.BeginErrorReadLine();
			_activeCompressionProcess.BeginOutputReadLine();
			await _activeCompressionProcess.WaitForExitAsync();
		}

		private void HandleCompressionResult()
		{
			if (_activeCompressionProcess?.ExitCode == 0)
			{
				StatusText.Text = "Compression complete!";
				MessageBox.Show($"Saved to:\n{_compressedOutputPath}", "Success");
			}
			else
			{
				StatusText.Text = "Error: Compression failed";
				SafeFileDelete(_compressedOutputPath);
			}
		}
		#endregion

		#region Utilities
		private void SetUiBusyState(bool isBusy)
		{
			StatusText.Text = isBusy ? "Compressing..." : "Ready";
			ProgressBar.IsIndeterminate = isBusy;
			CompressButton.IsEnabled = !isBusy;
		}

		private void HandleCompressionError(Exception ex)
		{
			StatusText.Text = $"Error: {ex.Message}";
			SafeFileDelete(_compressedOutputPath);
		}

		private void CleanupResources()
		{
			_activeCompressionProcess?.Dispose();
			_activeCompressionProcess = null;
			SetUiBusyState(false);
		}

		private void SafeFileDelete(string path)
		{
			try { File.Delete(path); } catch { /* Ignore deletion errors */ }
		}

		private void HandleWindowClosing(CancelEventArgs e)
		{
			if (_activeCompressionProcess == null || _activeCompressionProcess.HasExited)
				return;

			var confirm = MessageBox.Show(
				"Compression is running! Close and cancel?",
				"Warning",
				MessageBoxButton.YesNo,
				MessageBoxImage.Warning);

			if (confirm == MessageBoxResult.No)
			{
				e.Cancel = true;
				return;
			}

			_activeCompressionProcess.Kill();
			SafeFileDelete(_compressedOutputPath);
		}
		#endregion
	}
}