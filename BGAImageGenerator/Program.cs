using ConvertHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BGAImageGenerator
{
    internal static class Program
    {
        private sealed class Options
        {
            public string GraphicRoot = "";
            public string GraphicId = "";
            public string OutputFolder = "";
            public BgaImageOutputFormat Format = BgaImageOutputFormat.Mp4;
            public List<string> Layers = new List<string>();
        }

        private static int Main(string[] args)
        {
            string temporaryFolder = null;
            try
            {
                if (args.Length == 0 || args.Any(arg => arg == "--help" || arg == "-h"))
                {
                    ShowUsage();
                    return args.Length == 0 ? 1 : 0;
                }

                Options options = ParseOptions(args);
                string sourcePath = Path.GetFullPath(options.GraphicRoot);
                string graphicFolder;
                string outputName;
                if (IsIfsFile(sourcePath))
                {
                    temporaryFolder = Path.Combine(
                        Path.GetTempPath(), "scharfrichter-bga-ifs-" + Guid.NewGuid().ToString("N"));
                    graphicFolder = AfpGraphicArchiveExtractor.Extract(sourcePath, temporaryFolder);
                    outputName = Path.GetFileNameWithoutExtension(sourcePath);
                }
                else
                {
                    graphicFolder = ResolveGraphicFolder(sourcePath, options.GraphicId);
                    outputName = new DirectoryInfo(graphicFolder).Name;
                }
                IReadOnlyList<string> availableLayers = AfpBgaExporter.GetLayers(graphicFolder);
                if (availableLayers.Count == 0)
                    throw new InvalidOperationException("No numbered AFP layers were found.");

                IReadOnlyList<string> layers = ResolveLayers(options.Layers, availableLayers);
                string outputFolder = ResolveOutputFolder(options.OutputFolder, outputName);
                Directory.CreateDirectory(outputFolder);

                Console.WriteLine("BGA Image Generator");
                Console.WriteLine("  source  : " + sourcePath);
                Console.WriteLine("  format : " + options.Format.ToString().ToLowerInvariant());
                Console.WriteLine("  layers : " + String.Join(", ", layers));
                Console.WriteLine("  output : " + outputFolder);

                foreach (BgaImageExportResult result in AfpBgaExporter.ExportMany(
                    graphicFolder, layers, outputFolder, options.Format))
                {
                    string destination = result.OutputFiles.Count == 1
                        ? result.OutputFiles[0]
                        : Path.GetDirectoryName(result.OutputFiles[0]);
                    Console.WriteLine(
                        "  " + result.Layer + ": " + result.FrameCount + " frames, " + result.Fps + " fps -> " +
                        destination);
                }

                Console.WriteLine("BGA image export finished.");
                return 0;
            }
            catch (Exception error)
            {
                Exception report = error is AggregateException aggregate
                    ? aggregate.Flatten().InnerExceptions[0]
                    : error;
                Console.Error.WriteLine("ERROR: " + report.Message);
                return 1;
            }
            finally
            {
                if (temporaryFolder != null && Directory.Exists(temporaryFolder))
                    Directory.Delete(temporaryFolder, true);
            }
        }

        private static Options ParseOptions(string[] args)
        {
            Options options = new Options { GraphicRoot = args[0] };
            for (int i = 1; i < args.Length; i++)
            {
                string option = args[i];
                switch (option.ToLowerInvariant())
                {
                    case "--id":
                        options.GraphicId = ReadValue(args, ref i, option);
                        break;
                    case "--output":
                    case "-o":
                        options.OutputFolder = ReadValue(args, ref i, option);
                        break;
                    case "--format":
                    case "-f":
                        options.Format = ParseFormat(ReadValue(args, ref i, option));
                        break;
                    case "--layer":
                    case "-l":
                        foreach (string layer in ReadValue(args, ref i, option)
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string trimmed = layer.Trim();
                            if (trimmed.Length > 0 &&
                                !options.Layers.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                                options.Layers.Add(trimmed);
                        }
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + option);
                }
            }
            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            index++;
            if (index >= args.Length || args[index].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("A value is required for " + option + ".");
            return args[index];
        }

        private static BgaImageOutputFormat ParseFormat(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "mp4" => BgaImageOutputFormat.Mp4,
                "webm" => BgaImageOutputFormat.Webm,
                "png" => BgaImageOutputFormat.Png,
                "webp" => BgaImageOutputFormat.Webp,
                _ => throw new ArgumentException("Unsupported output format: " + value),
            };
        }

        private static string ResolveGraphicFolder(string rootOrFolder, string graphicId)
        {
            string root = Path.GetFullPath(rootOrFolder);
            if (IsGraphicFolder(root))
                return root;

            if (!String.IsNullOrWhiteSpace(graphicId))
            {
                string candidate = Path.Combine(root, graphicId);
                if (IsGraphicFolder(candidate))
                    return Path.GetFullPath(candidate);
            }

            throw new DirectoryNotFoundException(
                "Graphic folder was not found. Specify its parent with --id, or pass the ID folder directly.");
        }

        private static bool IsGraphicFolder(string folder)
        {
            return Directory.Exists(Path.Combine(folder, "afp")) &&
                Directory.Exists(Path.Combine(folder, "afp", "bsi")) &&
                Directory.Exists(Path.Combine(folder, "geo")) &&
                Directory.Exists(Path.Combine(folder, "tex"));
        }

        private static bool IsIfsFile(string path)
        {
            return File.Exists(path) &&
                String.Equals(Path.GetExtension(path), ".ifs", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<string> ResolveLayers(
            IReadOnlyList<string> requested,
            IReadOnlyList<string> available)
        {
            if (requested.Count == 0)
                return available;

            List<string> result = new List<string>();
            foreach (string layer in requested)
            {
                string match = available.FirstOrDefault(
                    candidate => String.Equals(candidate, layer, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    throw new ArgumentException(
                        "AFP layer was not found: " + layer + ". Available: " + String.Join(", ", available));
                result.Add(match);
            }
            return result;
        }

        private static string ResolveOutputFolder(string requested, string outputName)
        {
            if (!String.IsNullOrWhiteSpace(requested))
                return Path.GetFullPath(requested);

            return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "BGAImageOutput", outputName));
        }

        private static void ShowUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  BGAImageGenerator <graphic root/folder or IFS file> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --id <id>             Graphic ID when a parent folder is specified");
            Console.WriteLine("  --output, -o <folder> Output folder");
            Console.WriteLine("  --format, -f <format> mp4, webm, png, or webp (default: mp4)");
            Console.WriteLine("  --layer, -l <layers>  AFP layers such as 00 or 00,01 (default: all)");
            Console.WriteLine();
            Console.WriteLine("Output:");
            Console.WriteLine("  mp4/webm: <output>\\00.mp4 or <output>\\00.webm");
            Console.WriteLine("  png/webp: <output>\\00\\frame_1.png or frame_1.webp");
        }
    }
}
