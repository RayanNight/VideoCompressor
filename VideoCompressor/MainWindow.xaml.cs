using System.Diagnostics;
using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using VideoCompressor;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace VideoCompressor
{
	public partial class MainWindow : Window
	{
		// State tracking
		private string _sourceVideoPath = string.Empty;
		private string _compressedOutputPath = string.Empty;
		private Process? _activeCompressionProcess = null;
		DropShadowEffect originalEffect = new();
		DropShadowEffect modifiedEffect = new();
		

		public static LinearGradientBrush CompressButtonGradient { get; } = new LinearGradientBrush
		{
			StartPoint = new Point(0, 0),
			EndPoint = new Point(1, 1),
			GradientStops = new GradientStopCollection
			{
				new GradientStop(Color.FromRgb(0x9D, 0x4E, 0xDD), 0),
				new GradientStop(Color.FromRgb(0x4A, 0x00, 0xA8), 1)
			}
		};

		public MainWindow()
		{
			InitializeComponent();
			SetupEventHandlers();
		}

		private void SetupEventHandlers()
		{
			QualitySlider.ValueChanged += (_, _) =>
				QualityValueText.Text = $"CRF: {(int)QualitySlider.Value}";

			CompressButton.Background = Brushes.Gray;

			originalEffect = (DropShadowEffect)CompressButton.Effect.CloneCurrentValue();

			// Modify a copy
			modifiedEffect = (DropShadowEffect)originalEffect.Clone();
			modifiedEffect.Opacity = 0;
			CompressButton.Effect = modifiedEffect;

			Closing += (_, e) => HandleWindowClosing(e);
		}

		private void BrowseButton_Click(object sender, RoutedEventArgs e)
		{
			var openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "Video Files|*.mp4;*.avi;*.mov;*.mkv|All Files|*.*"
			};

			if (openFileDialog.ShowDialog() == true)
			{
				_sourceVideoPath = openFileDialog.FileName;

				// Initialize size estimate
				int crfValue = (int)QualitySlider.Value;
				string estimate = EstimateSize(_sourceVideoPath, crfValue);
				EstimatedSizeTextBlock.Text = $"Estimated size: {estimate}";

				// Get original file info
				var originalInfo = new FileInfo(_sourceVideoPath);
				double originalSizeMB = originalInfo.Length / (1024.0 * 1024.0);
				SelectedFileText.Text = Path.GetFileName(_sourceVideoPath + $" ({originalSizeMB.ToString("0.0")}MB)");

				CompressButton.Background = CompressButtonGradient;
				CompressButton.Effect = originalEffect;
			}
		}

		private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			// Update estimated size if we have a video file selected
			if (!string.IsNullOrEmpty(_sourceVideoPath) && File.Exists(_sourceVideoPath))
			{
				// Get the current CRF value (convert to integer since slider uses double)
				int crfValue = (int)QualitySlider.Value;

				string estimate = EstimateSize(_sourceVideoPath, crfValue);
				EstimatedSizeTextBlock.Text = $"Estimated size: {estimate}";
			}
		}

		private async void CompressButton_Click(object sender, RoutedEventArgs e)
		{
			if (!ValidateInputFile()) return;

			Brush BrowseButton_Color = BrowseButton.Background;

			BrowseButton.IsEnabled = false;
			QualitySlider.IsEnabled = false;
			GenerateSubtitlesCheckbox.IsEnabled = false;
			CompressButton.Background = Brushes.Gray;
			CompressButton.Effect = modifiedEffect;
			BrowseButton.Background = Brushes.Gray;

			PrepareOutputPath();
			if (!ConfirmOverwriteIfNeeded()) return;

			string directory = Path.GetDirectoryName(_sourceVideoPath);
			string fileName = Path.GetFileNameWithoutExtension(_sourceVideoPath);
			string tempAudioPath = await ExtractAudioAsync(_sourceVideoPath);

			string modelsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
			string fullModelPath = Path.Combine(modelsPath, "vosk-model-small-en-us-0.15");

			// Check if user wants to generate subtitles
			if (GenerateSubtitlesCheckbox.IsChecked == true)
			{
				SetUiBusyState(true);
				StatusText.Text = "Subtitle Generating...";

				//----
				// 1. Extract audio
				string tempAudio = await ExtractAudioAsync(_sourceVideoPath);

				// 2. Generate subtitles with progress updates
				string srtPath = Path.Combine(directory, $"subtitles({fileName}).srt");
				var progressHandler = new Progress<string>(message =>
				{
					// Update your UI here instead of MessageBox
					StatusText.Text = message;
					// Or use a progress bar if you have one
				});

				await SubtitleGenerator.GenerateSRTFromAudioAsync(tempAudio, srtPath);

				// 3. Clean up
				File.Delete(tempAudio);
				//---
			}

			SetUiBusyState(true);
			StatusText.Text = "Compressing Your Video...";

			await RunCompressionProcess();

			SetUiBusyState(false);
			StatusText.Text = "Finished!";


			BrowseButton.IsEnabled = true;
			QualitySlider.IsEnabled = true;
			GenerateSubtitlesCheckbox.IsEnabled = true;
			CompressButton.Background = CompressButtonGradient;
			CompressButton.Effect = originalEffect;
			BrowseButton.Background = BrowseButton_Color;
		}

		private async Task<string> ExtractAudioAsync(string videoPath)
		{
			string tempAudioPath = Path.GetTempFileName() + ".wav";

			using (var process = new Process())
			{
				process.StartInfo = new ProcessStartInfo
				{
					FileName = "ffmpeg.exe",
					Arguments = $"-y -i \"{videoPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{tempAudioPath}\"",
					UseShellExecute = false,
					CreateNoWindow = true
				};

				process.Start();
				await process.WaitForExitAsync();

				if (process.ExitCode != 0 || !File.Exists(tempAudioPath))
				{
					throw new Exception("Audio extraction failed");
				}
			}
			return tempAudioPath;
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
			string arguments = $"-i \"{_sourceVideoPath}\" -c:v libx264 -crf {quality} -preset faster -profile:v high -tune film -movflags +faststart -x264-params \"ref=4:aq-mode=2\" -c:a aac -b:a 128k \"{_compressedOutputPath}\"";

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
			//StatusText.Text = isBusy ? "Compressing..." : "Ready";
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


		public static string EstimateSize(string videoPath, int crfValue)
		{
			try
			{
				// Get original file info
				var originalInfo = new FileInfo(videoPath);
				double originalSizeMB = originalInfo.Length / (1024.0 * 1024.0);

				// Estimate based on CRF (this is approximate - you may need to adjust ratios)
				double compressionRatio = GetCompressionRatio(crfValue);
				double estimatedSizeMB = originalSizeMB * compressionRatio;

				return $"{estimatedSizeMB:F1} MB";
			}
			catch
			{
				return "Size estimation unavailable";
			}
		}

		private static double GetCompressionRatio(int crfValue)
		{
			return Math.Pow(0.85, crfValue - 17) * 0.4;
		}
	}
}