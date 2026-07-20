using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConvertHelper.Afp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ConvertHelper
{
    internal static class AfpBgaFrameRenderer
    {
        internal sealed class RenderSource
        {
            internal string AfpFolder = "";
            internal Dictionary<string, AfpShape> Shapes = new Dictionary<string, AfpShape>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, AfpTexture> Textures = new Dictionary<string, AfpTexture>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, AfpMovie> Movies = new Dictionary<string, AfpMovie>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, AfpMovie> MoviesByFile = new Dictionary<string, AfpMovie>(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class RenderAnimation
        {
            public string AfpFile = "";
            public string RenderPath = "";
            public int Fps;
            public int Width;
            public int Height;
            public IEnumerable<Rgba32[]> Frames = Array.Empty<Rgba32[]>();
        }

        internal sealed class RenderResult
        {
            public List<string> FrameFiles = new List<string>();
            public int Fps;
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
        }

        internal static RenderSource LoadSource(string graphicFolder)
        {
            if (String.IsNullOrWhiteSpace(graphicFolder))
                throw new ArgumentException("Graphic folder is required.", nameof(graphicFolder));

            graphicFolder = Path.GetFullPath(graphicFolder);
            string afpFolder = RequireDirectory(Path.Combine(graphicFolder, "afp"));
            string bsiFolder = RequireDirectory(Path.Combine(afpFolder, "bsi"));
            string geoFolder = RequireDirectory(Path.Combine(graphicFolder, "geo"));
            string textureFolder = RequireDirectory(Path.Combine(graphicFolder, "tex"));

            RenderSource source = new RenderSource
            {
                AfpFolder = afpFolder,
                Shapes = LoadShapes(geoFolder),
                Textures = LoadTextures(textureFolder),
            };
            LoadMovies(afpFolder, bsiFolder, source.Movies, source.MoviesByFile);
            return source;
        }

        internal static RenderAnimation CreateAnimation(RenderSource source, string afpName)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (String.IsNullOrWhiteSpace(afpName))
                throw new ArgumentException("AFP name is required.", nameof(afpName));

            string afpFileName = ResolveAfpName(source.AfpFolder, afpName.Trim());
            if (!source.MoviesByFile.TryGetValue(afpFileName, out AfpMovie targetMovie))
                throw new InvalidOperationException("AFP/BSI pair was not loaded: " + afpFileName);

            int fps = checked((int)Math.Round(targetMovie.Fps));
            if (fps <= 0)
                throw new InvalidDataException("AFP contains an invalid frame rate: " + targetMovie.Fps);

            AfpRuntimeRenderer renderer = new AfpRuntimeRenderer(source.Movies, source.Shapes, source.Textures);
            return new RenderAnimation
            {
                AfpFile = afpFileName,
                RenderPath = targetMovie.ExportedName,
                Fps = fps,
                Width = targetMovie.Width,
                Height = targetMovie.Height,
                Frames = renderer.Render(targetMovie),
            };
        }

        public static RenderResult RenderFrames(string graphicFolder, string afpName, string outputFolder, string outputPrefix)
        {
            return RenderFrames(LoadSource(graphicFolder), afpName, outputFolder, outputPrefix);
        }

        internal static RenderResult RenderFrames(RenderSource source, string afpName, string outputFolder, string outputPrefix)
        {
            if (String.IsNullOrWhiteSpace(outputPrefix))
                outputPrefix = "bga_image";

            outputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(outputFolder);
            RenderAnimation animation = CreateAnimation(source, afpName);

            foreach (string oldFrame in Directory.EnumerateFiles(outputFolder, outputPrefix + "_????.png"))
                File.Delete(oldFrame);

            List<string> frameFiles = new List<string>();
            PngEncoder encoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
            int frameIndex = 0;
            foreach (Rgba32[] pixels in animation.Frames)
            {
                string frameFile = Path.Combine(outputFolder, outputPrefix + "_" + frameIndex.ToString("0000", CultureInfo.InvariantCulture) + ".png");
                using Image<Rgba32> image = Image.LoadPixelData(pixels, animation.Width, animation.Height);
                image.Save(frameFile, encoder);
                frameFiles.Add(Path.GetFullPath(frameFile));
                frameIndex++;
            }

            if (frameFiles.Count == 0)
                throw new InvalidOperationException("AFP path did not render any frames: " + animation.RenderPath);

            RenderManifest manifest = new RenderManifest
            {
                AfpFile = animation.AfpFile,
                RenderPath = animation.RenderPath,
                Fps = animation.Fps,
                FrameCount = frameFiles.Count,
                Frames = frameFiles,
            };
            string manifestFile = Path.GetFullPath(Path.Combine(outputFolder, outputPrefix + "_manifest.json"));
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return new RenderResult
            {
                Fps = animation.Fps,
                FrameFiles = frameFiles,
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

        private static void LoadMovies(
            string afpFolder,
            string bsiFolder,
            Dictionary<string, AfpMovie> movies,
            Dictionary<string, AfpMovie> moviesByFile)
        {
            foreach (string sourceFile in Directory.EnumerateFiles(afpFolder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(sourceFile);
                string bsiFile = Path.Combine(bsiFolder, fileName);
                if (!File.Exists(bsiFile))
                    continue;

                AfpMovie movie = AfpBinaryParser.ParseMovie(fileName, File.ReadAllBytes(sourceFile), File.ReadAllBytes(bsiFile));
                movies[movie.ExportedName] = movie;
                movies[fileName] = movie;
                moviesByFile[fileName] = movie;
            }
        }

        private static string ResolveAfpName(string folder, string requested)
        {
            string[] candidates = requested.EndsWith(".afp", StringComparison.OrdinalIgnoreCase)
                ? new[] { requested, Path.GetFileNameWithoutExtension(requested) }
                : new[] { requested, Path.GetFileNameWithoutExtension(requested), requested + ".afp" };
            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(folder, candidate);
                if (File.Exists(path))
                    return Path.GetFileName(path);
            }
            throw new FileNotFoundException("AFP file was not found: " + requested);
        }

        private static string RequireDirectory(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("Required graphic directory was not found: " + path);
            return path;
        }
    }
}
