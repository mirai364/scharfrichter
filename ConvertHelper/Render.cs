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

            Dictionary<string, Sound[]> sounds = new Dictionary<string, Sound[]>();
            Chart[] charts = null;
            bool cancel = false;
            string outFile = "0001";
            string targetPath = null;
            string IIDXDBName = Path.GetFileNameWithoutExtension(args[0]);
            string title = IIDXDBName;
            string version = IIDXDBName.Substring(0, 2);

            if (IIDXDBName.Contains("pre"))
            {
                IIDXDBName = IIDXDBName.Substring(0, 5);
            }
            if (IIDXDBName.Length > 5)
            {
                IIDXDBName = IIDXDBName.Substring(0, 5);
            }
            while (IIDXDBName.StartsWith("0"))
                IIDXDBName = IIDXDBName.Substring(1);

            if (db[IIDXDBName]["TITLE"] != "")
            {
                title = db[IIDXDBName]["TITLE"];
                title = Common.nameReplace(title);
            }

            foreach (string filename in args)
            {
                if (cancel)
                    break;

                string tmp = Path.GetFileNameWithoutExtension(filename);
                string INDEX = "0";
                if (tmp.Contains("pre"))
                {
                    continue;
                }
                if (tmp.Length > 5)
                {
                    INDEX = tmp.Substring(5);
                }

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
                            if (!sounds.ContainsKey(INDEX))
                            {
                                using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(filename)))
                                {
                                    sounds.Add(INDEX, Bemani2DX.Read(mem).Sounds);
                                }
                            }
                            break;
                        case @".S3P":
                            if (!sounds.ContainsKey(INDEX))
                            {
                                using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(filename)))
                                {
                                    sounds.Add(INDEX, BemaniS3P.Read(mem).Sounds);
                                }
                            }
                            break;
                    }
                }
            }

            if (!cancel && (sounds != null) && (charts != null))
            {
                List<byte[]> rendered = new List<byte[]>();
                List<int> renderedIndex = new List<int>();

                for (int k = 0; k < charts.Length; k++)
                {
                    string keySet = "0";
                    if (k < 6)
                    {
                        keySet = db[IIDXDBName]["KEYSETSP" + config["IIDX"]["DIFFICULTY" + k.ToString()]];
                    }
                    else if (k < 12)
                    {
                        keySet = db[IIDXDBName]["KEYSETDP" + config["IIDX"]["DIFFICULTY" + k.ToString()]];
                    }
                    Chart chart = charts[k];

                    if (chart == null)
                        continue;


                    Console.WriteLine("");
                    Console.WriteLine("Rendering " + k.ToString());
                    Console.WriteLine("Use keySet " + keySet);
                    Sound[] tmpSound;
                    if (!sounds.TryGetValue(keySet, out tmpSound))
                    {
                        Console.WriteLine("not found keySet");
                        if (!sounds.TryGetValue("0", out tmpSound))
                        {
                            Console.WriteLine("not found sounds \n continue");
                            continue;
                        }
                    }

                    byte[] data = ChartRenderer.Render(chart, tmpSound);

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
                    if (idUseRenderAutoTip)
                    {
                        string targetFolder = "sounds";
                        if (keySet != "0")
                            targetFolder = "sounds_" + keySet;
                        targetPath = Path.Combine(output, version, title, targetFolder);
                        match = false;
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
