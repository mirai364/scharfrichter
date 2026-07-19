using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ConvertHelper.Afp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ConvertHelper
{
    internal static class AfpBgaFrameRenderer
    {
        internal sealed class RenderResult
        {
            public List<string> FrameFiles = new List<string>();
            public int Fps;
            public string WebmFile = "";
            public string ManifestFile = "";
        }

        private sealed class RenderManifest
        {
            public string Renderer { get; set; } = "Scharfrichter C# AFP renderer";
            public string AfpFile { get; set; } = "";
            public string RenderPath { get; set; } = "";
            public int Fps { get; set; }
            public int FrameCount { get; set; }
            public List<string> Frames { get; set; } = new List<string>();
            public string Webm { get; set; } = "";
        }

        public static RenderResult RenderFrames(string graphicFolder, string afpName, string outputFolder, string outputPrefix)
        {
            if (String.IsNullOrWhiteSpace(graphicFolder))
                throw new ArgumentException("Graphic folder is required.", nameof(graphicFolder));
            if (String.IsNullOrWhiteSpace(afpName))
                throw new ArgumentException("AFP name is required.", nameof(afpName));
            if (String.IsNullOrWhiteSpace(outputPrefix))
                outputPrefix = "bga_image";

            graphicFolder = Path.GetFullPath(graphicFolder);
            outputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(outputFolder);

            string afpFolder = RequireDirectory(Path.Combine(graphicFolder, "afp"));
            string bsiFolder = RequireDirectory(Path.Combine(afpFolder, "bsi"));
            string geoFolder = RequireDirectory(Path.Combine(graphicFolder, "geo"));
            string textureFolder = RequireDirectory(Path.Combine(graphicFolder, "tex"));
            string afpFileName = ResolveAfpName(afpFolder, afpName.Trim());

            Dictionary<string, AfpShape> shapes = LoadShapes(geoFolder);
            Dictionary<string, AfpTexture> textures = LoadTextures(textureFolder);
            Dictionary<string, AfpMovie> movies = LoadMovies(afpFolder, bsiFolder, afpFileName, out AfpMovie targetMovie);
            int fps = checked((int)Math.Round(targetMovie.Fps));
            if (fps <= 0)
                throw new InvalidDataException("AFP contains an invalid frame rate: " + targetMovie.Fps);

            foreach (string oldFrame in Directory.EnumerateFiles(outputFolder, outputPrefix + "_????.png"))
                File.Delete(oldFrame);

            AfpRuntimeRenderer renderer = new AfpRuntimeRenderer(movies, shapes, textures);
            List<string> frameFiles = new List<string>();
            PngEncoder encoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
            int frameIndex = 0;
            foreach (Rgba32[] pixels in renderer.Render(targetMovie))
            {
                string frameFile = Path.Combine(outputFolder, outputPrefix + "_" + frameIndex.ToString("0000", CultureInfo.InvariantCulture) + ".png");
                using Image<Rgba32> image = Image.LoadPixelData(pixels, targetMovie.Width, targetMovie.Height);
                image.Save(frameFile, encoder);
                frameFiles.Add(Path.GetFullPath(frameFile));
                frameIndex++;
            }

            if (frameFiles.Count == 0)
                throw new InvalidOperationException("AFP path did not render any frames: " + targetMovie.ExportedName);

            string webmFile = Path.GetFullPath(Path.Combine(outputFolder, outputPrefix + ".webm"));
            CreateTransparentWebm(outputFolder, outputPrefix, targetMovie.Fps, webmFile);

            RenderManifest manifest = new RenderManifest
            {
                AfpFile = afpFileName,
                RenderPath = targetMovie.ExportedName,
                Fps = fps,
                FrameCount = frameFiles.Count,
                Frames = frameFiles,
                Webm = webmFile,
            };
            string manifestFile = Path.GetFullPath(Path.Combine(outputFolder, outputPrefix + "_manifest.json"));
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return new RenderResult
            {
                Fps = fps,
                FrameFiles = frameFiles,
                WebmFile = webmFile,
                ManifestFile = manifestFile,
            };
        }

        private static Dictionary<string, AfpShape> LoadShapes(string folder)
        {
            Dictionary<string, AfpShape> shapes = new Dictionary<string, AfpShape>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(folder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string reference = Path.GetFileName(file);
                shapes[reference] = AfpBinaryParser.ParseShape(reference, File.ReadAllBytes(file));
            }
            return shapes;
        }

        private static Dictionary<string, AfpTexture> LoadTextures(string folder)
        {
            Dictionary<string, AfpTexture> textures = new Dictionary<string, AfpTexture>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                using Image<Rgba32> image = Image.Load<Rgba32>(file);
                Rgba32[] pixels = new Rgba32[image.Width * image.Height];
                image.CopyPixelDataTo(pixels);
                string name = Path.GetFileNameWithoutExtension(file);
                textures[name] = new AfpTexture { Name = name, Width = image.Width, Height = image.Height, Pixels = pixels };
            }
            return textures;
        }

        private static Dictionary<string, AfpMovie> LoadMovies(
            string afpFolder, string bsiFolder, string targetFileName, out AfpMovie targetMovie)
        {
            Dictionary<string, AfpMovie> movies = new Dictionary<string, AfpMovie>(StringComparer.OrdinalIgnoreCase);
            targetMovie = null;
            foreach (string sourceFile in Directory.EnumerateFiles(afpFolder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(sourceFile);
                string bsiFile = Path.Combine(bsiFolder, fileName);
                if (!File.Exists(bsiFile)) continue;

                AfpMovie movie = AfpBinaryParser.ParseMovie(fileName, File.ReadAllBytes(sourceFile), File.ReadAllBytes(bsiFile));
                movies[movie.ExportedName] = movie;
                movies[fileName] = movie;
                if (String.Equals(fileName, targetFileName, StringComparison.OrdinalIgnoreCase))
                    targetMovie = movie;
            }
            if (targetMovie == null)
                throw new InvalidOperationException("AFP/BSI pair was not loaded: " + targetFileName);
            return movies;
        }

        private static string ResolveAfpName(string folder, string requested)
        {
            string[] candidates = requested.EndsWith(".afp", StringComparison.OrdinalIgnoreCase)
                ? new[] { requested, Path.GetFileNameWithoutExtension(requested) }
                : new[] { requested, Path.GetFileNameWithoutExtension(requested), requested + ".afp" };
            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(folder, candidate);
                if (File.Exists(path)) return Path.GetFileName(path);
            }
            throw new FileNotFoundException("AFP file was not found: " + requested);
        }

        private static string RequireDirectory(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("Required graphic directory was not found: " + path);
            return path;
        }

        private static void CreateTransparentWebm(string outputFolder, string prefix, double fps, string outputFile)
        {
            string input = Path.Combine(outputFolder, prefix + "_%04d.png");
            string[] arguments =
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-framerate", fps.ToString("G", CultureInfo.InvariantCulture),
                "-i", input,
                "-c:v", "libvpx-vp9", "-lossless", "1", "-pix_fmt", "yuva420p",
                "-auto-alt-ref", "0", "-metadata:s:v:0", "alpha_mode=1", outputFile,
            };
            ProcessResult process = RunProcess("ffmpeg", arguments);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("ffmpeg failed to create transparent WebM: " + process.StandardError.Trim());
        }

        private sealed class ProcessResult
        {
            public int ExitCode;
            public string StandardOutput = "";
            public string StandardError = "";
        }

        private static ProcessResult RunProcess(string executable, string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using Process process = new Process { StartInfo = startInfo };
            process.Start();
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = standardOutput.Result,
                StandardError = standardError.Result,
            };
        }
    }
}
