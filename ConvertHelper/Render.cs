using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using Scharfrichter.Common;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ConvertHelper
{

    static public class Render
    {
        static public void RenderWAV(string[] inArgs, long unitNumerator, long unitDenominator)
        {
            // configuration
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            Configuration db = Common.LoadDB();
            bool idUseRenderAutoTip = config["BMS"].GetBool("IsUseRenderAutoTip");
            Dictionary<int, int> ignore = new Dictionary<int, int>();
            if (idUseRenderAutoTip)
            {
                ignore.Add(1, 1);
                ignore.Add(2, 2);
            }

            Splash.Show("Render");
            Console.WriteLine("Timing: " + unitNumerator.ToString() + "/" + unitDenominator.ToString());

            string output = config["BMS"]["Output"];

            string[] args;

            if (inArgs.Length > 0)
                args = Subfolder.Parse(inArgs);
            else
                args = inArgs;

            if (System.Diagnostics.Debugger.IsAttached && args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Debugger attached. Input file name:");
                args = new string[] { Console.ReadLine() };
            }

            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Usage: Render2DX <files..>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("You must have both the chart file (.1) and the sound file (.2dx).");
                Console.WriteLine("Supported formats:");
                Console.WriteLine("1, 2DX");
            }

            Sound[] sounds = null;
            Chart[] charts = null;
            bool cancel = false;
            string outFile = "0001";
            string targetPath = null;

            foreach (string filename in args)
            {
                if (cancel)
                    break;

                if (File.Exists(filename))
                {
                    switch (Path.GetExtension(filename).ToUpper())
                    {
                        case @".1":
                            if (charts == null)
                            {
                                Console.WriteLine();
                                Console.WriteLine("Valid charts:");
                                if (!idUseRenderAutoTip)
                                    outFile = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename));
                                using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(filename)))
                                {
                                    charts = Bemani1.Read(mem, unitNumerator, unitDenominator, ignore).Charts;
                                    for (int i = 0; i < charts.Length; i++)
                                    {
                                        if (charts[i] != null)
                                            Console.Write(i.ToString() + "  ");
                                    }
                                }
                                Console.WriteLine();
                            }
                            break;
                        case @".2DX":
                            if (sounds == null)
                            {
                                using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(filename)))
                                {
                                    sounds = Bemani2DX.Read(mem).Sounds;
                                }
                            }
                            break;
                    }
                }
            }

            if (!cancel && (sounds != null) && (charts != null))
            {

                if (idUseRenderAutoTip)
                {
                    string IIDXDBName = Path.GetFileNameWithoutExtension(args[1]);
                    string title = IIDXDBName;
                    string version = IIDXDBName.Substring(0, 2);
                    string INDEX = null;
                    if (IIDXDBName.Length > 5)
                    {
                        INDEX = IIDXDBName.Substring(5);
                        IIDXDBName = IIDXDBName.Substring(0, 5);
                    }
                    while (IIDXDBName.StartsWith("0"))
                        IIDXDBName = IIDXDBName.Substring(1);

                    if (db[IIDXDBName]["TITLE"] != "")
                    {
                        title = db[IIDXDBName]["TITLE"];
                        title = Common.nameReplace(title);
                    }
                    targetPath = Path.Combine(output, version, title, "sounds");
                }

                List<byte[]> rendered = new List<byte[]>();
                List<int> renderedIndex = new List<int>();

                for (int k = 0; k < charts.Length; k++)
                {
                    Chart chart = charts[k];

                    if (chart == null)
                        continue;

                    Console.WriteLine("Rendering " + k.ToString());
                    byte[] data = ChartRenderer.Render(chart, sounds);

                    int renderedCount = rendered.Count;
                    int matchIndex = -1;
                    bool match = false;

                    for (int i = 0; i < renderedCount; i++)
                    {
                        int renderedLength = rendered[i].Length;
                        if (renderedLength == data.Length)
                        {
                            byte[] renderedBytes = rendered[i];
                            match = true;
                            for (int j = 0; j < renderedLength; j++)
                            {
                                if (renderedBytes[j] != data[j])
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                            {
                                matchIndex = i;
                                break;
                            }
                        }
                    }

                    if (!match)
                    {
                        if (idUseRenderAutoTip)
                        {
                            Console.WriteLine("Writing unique " + (k < 6 ? 1 : 3) + config["IIDX"].GetValue("DIFFICULTY" + k.ToString()));
                            Common.SafeCreateDirectory(targetPath);
                            File.WriteAllBytes(targetPath + "\\" + outFile + "-" + (k < 6 ? 1 : 3) + config["IIDX"].GetValue("DIFFICULTY" + k.ToString()) + ".wav", data);
                        }
                        else
                        {
                            Console.WriteLine("Writing unique " + k.ToString());
                            File.WriteAllBytes(outFile + " -" + Util.ConvertToDecimalString(k, 2) + ".wav", data);
                        }
                        rendered.Add(data);
                        renderedIndex.Add(k);
                    }
                    else
                    {
                        Console.WriteLine("Matches " + renderedIndex[matchIndex].ToString());
                    }
                }
            }
        }
    }
}
