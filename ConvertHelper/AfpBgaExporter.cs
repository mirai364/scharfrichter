using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ConvertHelper
{
    public enum BgaImageOutputFormat
    {
        Mp4,
        Webm,
        Png,
        Webp,
    }

    public sealed class BgaImageExportResult
    {
        public string Layer { get; internal set; } = "";
        public BgaImageOutputFormat Format { get; internal set; }
        public int Fps { get; internal set; }
        public int FrameCount { get; internal set; }
        public IReadOnlyList<string> OutputFiles { get; internal set; } = Array.Empty<string>();
    }

    public static class AfpBgaExporter
    {
        public static IReadOnlyList<string> GetLayers(string graphicFolder)
        {
            if (String.IsNullOrWhiteSpace(graphicFolder))
                throw new ArgumentException("Graphic folder is required.", nameof(graphicFolder));

            string afpFolder = Path.Combine(Path.GetFullPath(graphicFolder), "afp");
            string bsiFolder = Path.Combine(afpFolder, "bsi");
            if (!Directory.Exists(afpFolder) || !Directory.Exists(bsiFolder))
                throw new DirectoryNotFoundException("AFP/BSI directory was not found: " + afpFolder);

            SortedSet<string> layers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sourceFile in Directory.EnumerateFiles(afpFolder))
            {
                string fileName = Path.GetFileName(sourceFile);
                if (!File.Exists(Path.Combine(bsiFolder, fileName)))
                    continue;

                string layer = fileName.EndsWith(".afp", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(fileName)
                    : fileName;
                if (layer.Length > 0 && layer.All(Char.IsDigit))
                    layers.Add(layer);
            }
            return layers.ToList();
        }

        public static BgaImageExportResult Export(
            string graphicFolder,
            string layer,
            string outputFolder,
            BgaImageOutputFormat format = BgaImageOutputFormat.Mp4)
        {
            return ExportMany(graphicFolder, new[] { layer }, outputFolder, format)[0];
        }

        public static IReadOnlyList<BgaImageExportResult> ExportMany(
            string graphicFolder,
            IEnumerable<string> layers,
            string outputFolder,
            BgaImageOutputFormat format = BgaImageOutputFormat.Mp4)
        {
            if (layers == null)
                throw new ArgumentNullException(nameof(layers));
            if (String.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Output folder is required.", nameof(outputFolder));

            List<string> requestedLayers = layers
                .Select(layer => layer?.Trim() ?? "")
                .Where(layer => layer.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (requestedLayers.Count == 0)
                throw new ArgumentException("At least one layer is required.", nameof(layers));

            outputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(outputFolder);

            // GEO, textures, AFP and BSI are shared by every selected layer.
            Stopwatch loadTimer = Stopwatch.StartNew();
            AfpBgaFrameRenderer.RenderSource source = AfpBgaFrameRenderer.LoadSource(graphicFolder);
            loadTimer.Stop();
            if (Environment.GetEnvironmentVariable("BGA_IMAGE_PROFILE") == "1")
                Console.Error.WriteLine("[profile] asset-load: " + loadTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms");
            BgaImageExportResult[] results = new BgaImageExportResult[requestedLayers.Count];
            Parallel.For(
                0,
                requestedLayers.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(2, requestedLayers.Count) },
                index =>
                {
                    results[index] = ExportLoaded(source, requestedLayers[index], outputFolder, format);
                });
            return results;
        }

        private static BgaImageExportResult ExportLoaded(
            AfpBgaFrameRenderer.RenderSource source,
            string layer,
            string outputFolder,
            BgaImageOutputFormat format)
        {
            AfpBgaFrameRenderer.RenderAnimation animation = AfpBgaFrameRenderer.CreateAnimation(source, layer);
            List<string> outputs;
            int frameCount;
            switch (format)
            {
                case BgaImageOutputFormat.Mp4:
                    outputs = ExportVideo(animation, outputFolder, layer, false, out frameCount);
                    break;
                case BgaImageOutputFormat.Webm:
                    outputs = ExportVideo(animation, outputFolder, layer, true, out frameCount);
                    break;
                case BgaImageOutputFormat.Png:
                    outputs = ExportImageFrames(animation, outputFolder, layer, false, out frameCount);
                    break;
                case BgaImageOutputFormat.Webp:
                    outputs = ExportImageFrames(animation, outputFolder, layer, true, out frameCount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }

            return new BgaImageExportResult
            {
                Layer = layer,
                Format = format,
                Fps = animation.Fps,
                FrameCount = frameCount,
                OutputFiles = outputs,
            };
        }

        private static List<string> ExportVideo(
            AfpBgaFrameRenderer.RenderAnimation animation,
            string outputFolder,
            string layer,
            bool webm,
            out int frameCount)
        {
            string extension = webm ? ".webm" : ".mp4";
            string outputFile = Path.GetFullPath(Path.Combine(outputFolder, layer + extension));
            List<string> arguments = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "rawvideo",
                "-pixel_format", "rgba",
                "-video_size", animation.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                    animation.Height.ToString(CultureInfo.InvariantCulture),
                "-framerate", animation.Fps.ToString(CultureInfo.InvariantCulture),
                "-i", "pipe:0",
            };
            if (webm)
            {
                arguments.AddRange(new[]
                {
                    "-c:v", "libvpx", "-crf", "4", "-b:v", "0", "-pix_fmt", "yuva420p",
                    "-auto-alt-ref", "0", "-metadata:s:v:0", "alpha_mode=1",
                });
            }
            else
            {
                arguments.AddRange(new[]
                {
                    "-c:v", "libx264", "-preset", "medium", "-crf", "12",
                    "-pix_fmt", "yuv420p", "-movflags", "+faststart",
                });
            }
            arguments.Add(outputFile);

            using Process process = StartFfmpeg(arguments, true);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            frameCount = 0;
            Exception writeError = null;
            try
            {
                Stream input = process.StandardInput.BaseStream;
                int expectedPixels = checked(animation.Width * animation.Height);
                Stopwatch renderTimer = new Stopwatch();
                Stopwatch pipeTimer = new Stopwatch();
                using IEnumerator<Rgba32[]> frames = animation.Frames.GetEnumerator();
                while (true)
                {
                    renderTimer.Start();
                    bool hasFrame = frames.MoveNext();
                    renderTimer.Stop();
                    if (!hasFrame)
                        break;

                    Rgba32[] pixels = frames.Current;
                    if (pixels.Length != expectedPixels)
                        throw new InvalidDataException("Renderer returned an unexpected frame size.");
                    pipeTimer.Start();
                    input.Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
                    pipeTimer.Stop();
                    frameCount++;
                }
                if (Environment.GetEnvironmentVariable("BGA_IMAGE_PROFILE") == "1")
                {
                    Console.Error.WriteLine(
                        "[profile] " + layer + " render: " +
                        renderTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) +
                        " ms; ffmpeg-pipe: " +
                        pipeTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms");
                }
            }
            catch (Exception error)
            {
                writeError = error;
            }
            finally
            {
                process.StandardInput.Close();
            }

            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            if (writeError != null)
                throw new InvalidOperationException(
                    "Failed while streaming frames to ffmpeg: " + standardError.Result.Trim(), writeError);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "ffmpeg failed to create " + extension + ": " + standardError.Result.Trim());
            if (frameCount == 0)
                throw new InvalidOperationException("AFP path did not render any frames: " + animation.RenderPath);

            return new List<string> { outputFile };
        }

        private static List<string> ExportImageFrames(
            AfpBgaFrameRenderer.RenderAnimation animation,
            string outputFolder,
            string layer,
            bool webp,
            out int frameCount)
        {
            string extension = webp ? ".webp" : ".png";
            string layerFolder = PrepareLayerFolder(outputFolder, layer, extension);
            List<string> outputs = new List<string>();
            PngEncoder pngEncoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
            WebpEncoder webpEncoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossless };

            frameCount = 0;
            foreach (Rgba32[] pixels in animation.Frames)
            {
                string outputFile = Path.GetFullPath(Path.Combine(
                    layerFolder,
                    "frame_" + (frameCount + 1).ToString(CultureInfo.InvariantCulture) + extension));
                using Image<Rgba32> image = Image.LoadPixelData(pixels, animation.Width, animation.Height);
                if (webp)
                    image.Save(outputFile, webpEncoder);
                else
                    image.Save(outputFile, pngEncoder);
                outputs.Add(outputFile);
                frameCount++;
            }

            if (frameCount == 0)
                throw new InvalidOperationException("AFP path did not render any frames: " + animation.RenderPath);
            return outputs;
        }

        private static string PrepareLayerFolder(string outputFolder, string layer, string extension)
        {
            string layerFolder = Path.GetFullPath(Path.Combine(outputFolder, layer));
            Directory.CreateDirectory(layerFolder);
            foreach (string oldFile in Directory.EnumerateFiles(layerFolder, "frame_*" + extension))
                File.Delete(oldFile);
            return layerFolder;
        }

        private static Process StartFfmpeg(IEnumerable<string> arguments, bool redirectInput)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            try
            {
                Process process = new Process { StartInfo = startInfo };
                process.Start();
                return process;
            }
            catch (Win32Exception error)
            {
                throw new InvalidOperationException("ffmpeg was not found in PATH.", error);
            }
        }
    }
}
