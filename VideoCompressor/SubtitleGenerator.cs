using NAudio.Wave;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Vosk;

namespace VideoCompressor
{
	public static class SubtitleGenerator
	{
		public static async Task GenerateSRTFromAudioAsync(string audioPath, string outputPath, Action<string> progressCallback = null)
		{
			try
			{
				progressCallback?.Invoke("Starting subtitle generation...");

				// Run the heavy work in background
				await Task.Run(() =>
				{
					var modelPath = "Models\\vosk-model-small-en-us-0.15";

					using (var model = new Model(modelPath))
					using (var recognizer = new VoskRecognizer(model, 16000.0f))
					using (var waveStream = new WaveFileReader(audioPath))
					{
						recognizer.SetWords(true);
						var subtitles = new System.Collections.Generic.List<SubtitleItem>();
						byte[] buffer = new byte[4096];
						int bytesRead;

						while ((bytesRead = waveStream.Read(buffer, 0, buffer.Length)) > 0)
						{
							if (recognizer.AcceptWaveform(buffer, bytesRead))
							{
								ParseResult(recognizer.Result(), subtitles);
							}
						}
						ParseResult(recognizer.FinalResult(), subtitles);

						if (subtitles.Count > 0)
						{
							WriteSRTFile(subtitles, outputPath);
							progressCallback?.Invoke($"Generated {subtitles.Count} subtitle entries");
						}
					}
				});

				MessageBox.Show("Subtitles generated successfully!", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error: {ex.Message}", "Generation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}	

		private static List<SubtitleItem> ProcessAudio(string audioPath, VoskRecognizer recognizer)
		{
			var subtitles = new List<SubtitleItem>();

			using (var waveStream = new WaveFileReader(audioPath))
			{
				// Verify audio format
				if (waveStream.WaveFormat.SampleRate != 16000 ||
					waveStream.WaveFormat.Channels != 1)
				{
					throw new Exception("Audio must be 16kHz mono WAV format");
				}

				byte[] buffer = new byte[4096];
				int bytesRead;

				while ((bytesRead = waveStream.Read(buffer, 0, buffer.Length)) > 0)
				{
					if (recognizer.AcceptWaveform(buffer, bytesRead))
					{
						var result = recognizer.Result();
						ParseResult(result, subtitles);
					}
				}

				// Process final result
				ParseResult(recognizer.FinalResult(), subtitles);
			}

			return subtitles;
		}

		private static void ParseResult(string jsonResult, List<SubtitleItem> subtitles)
		{
			try
			{
				dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonResult);
				if (result?.result == null) return;

				string text = result.text;
				if (string.IsNullOrWhiteSpace(text)) return;

				var words = result.result;
				if (words.Count == 0) return;

				subtitles.Add(new SubtitleItem
				{
					StartTime = TimeSpan.FromSeconds((double)words[0].start),
					EndTime = TimeSpan.FromSeconds((double)words[words.Count - 1].end),
					Text = text
				});
			}
			catch { /* Skip parsing errors */ }
		}

		private static void WriteSRTFile(List<SubtitleItem> subtitles, string outputPath)
		{
			try
			{
				// Ensure directory exists
				Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

				// Write with UTF-8 encoding
				using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
				{
					for (int i = 0; i < subtitles.Count; i++)
					{
						writer.WriteLine($"{i + 1}");
						writer.WriteLine($"{FormatTime(subtitles[i].StartTime)} --> {FormatTime(subtitles[i].EndTime)}");
						writer.WriteLine(subtitles[i].Text);
						writer.WriteLine();
					}
				}
			}
			catch (Exception ex)
			{
				throw new Exception($"Failed to write SRT file: {ex.Message}");
			}
		}

		private static string FormatTime(TimeSpan time)
		{
			return $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
		}

		private class SubtitleItem
		{
			public TimeSpan StartTime { get; set; }
			public TimeSpan EndTime { get; set; }
			public string Text { get; set; }
		}
	}
}