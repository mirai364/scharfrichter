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
using System.Text.RegularExpressions;

namespace ConvertHelper
{
    static public class BemaniToSM
    {
        private const string configFileName = "Convert";
        private const string databaseFileName = "musicdb";
        public struct Header
        {
            public int FilePathAddrStart { get; }
            public int FileAddrStart { get; }
            public int FileType { get; }
            public int FileSize { get; }
            public string FilePath { get; set; }

            public Header(BinaryReader reader)
            {
                FilePathAddrStart = reader.ReadInt32();
                FileAddrStart = reader.ReadInt32();
                FileType = reader.ReadInt32();
                FileSize = reader.ReadInt32();
                FilePath = "";
            }

            public void Print()
            {
                Console.WriteLine("FilePathAddr " + FilePathAddrStart.ToString("X"));
                Console.WriteLine("FileStartAddr " + FileAddrStart.ToString("X"));
                Console.WriteLine("FileEndAddr " + (FileAddrStart + FileSize).ToString("X"));
                Console.WriteLine("FileType " + FileType);
            }
        }

        static public void Convert(string[] inArgs)
        {
            // configuration
            Configuration config = Configuration.LoadDDRConfig(Common.configFileName);
            Configuration db = LoadDB();

            // splash
            Splash.Show( "Bemani To Stepmania" );

            // parse args
            string[] args;
            if( inArgs.Length > 0 )
                args = Subfolder.Parse(inArgs);
            else
                args = inArgs;

            // usage if no args present
            if( args.Length == 0 )
            {
                Console.WriteLine();
                Console.WriteLine( "Usage: BemaniToSM <input file>" );
                Console.WriteLine();
                Console.WriteLine( "Drag and drop with files and folders is fully supported for this application." );
                Console.WriteLine();
                Console.WriteLine( "Supported formats:" );
                Console.WriteLine( "SSQ, XWB" );
            }

            string outputFolder = config["SM"]["Output"];
            string movieFolder = config["SM"]["MovieFolder"];

            // process
            foreach ( string filename in args )
            {
                if( File.Exists(filename) )
                {
                    Console.WriteLine();
                    Console.WriteLine( "Processing File: " + filename );
                    string directory = Path.GetDirectoryName(filename);
                    if (outputFolder != "")
                        directory = outputFolder;

                    switch ( Path.GetExtension(filename).ToUpper() )
                    {
                        case @".ARC":
                            {
                                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    Console.WriteLine("Reading ARC File");

                                    string songId = Path.GetFileNameWithoutExtension(@filename);
                                    string title = "";
                                    string targetPath = directory;
                                    string trimSongId = songId.Split('_')[0].Trim();
                                    if (songId.Contains("_jk") && db[trimSongId]["TITLE"] != "")
                                    {
                                        title = db[trimSongId]["TITLE"];
                                        title = Common.nameReplace(title);
                                        string series = db[trimSongId]["series"];
                                        targetPath = Path.Combine(directory, series, title);
                                        Common.SafeCreateDirectory(targetPath);
                                    }

                                    using (BinaryReader reader = new BinaryReader(fs))
                                    {
                                        int version = reader.ReadInt32();
                                        int minVersion = reader.ReadInt32();
                                        int fileNum = reader.ReadInt32();
                                        reader.ReadInt32();

                                        var headerTable = new Dictionary<int, Header>();
                                        for (int i=0;i< fileNum;i++)
                                        {
                                            Header data = new Header(reader);
                                            headerTable.Add(i, data);
                                        }

                                        int headerEndAddr = headerTable[0].FileAddrStart - 1;
                                        foreach (KeyValuePair<int, Header> data in headerTable.Reverse())
                                        {
                                            reader.BaseStream.Position = data.Value.FilePathAddrStart;
                                            string filePath = new string(reader.ReadChars(headerEndAddr - data.Value.FilePathAddrStart));
                                            List<string> list = filePath.Trim().Split('/').ToList();
                                            string targetFilePath = list.Last();
                                            targetFilePath = Regex.Replace(targetFilePath, @"[^\w\.@-]", "");
                                            targetFilePath = Common.nameReplace(targetFilePath);
 
                                             Console.WriteLine(targetFilePath);

                                            reader.BaseStream.Position = data.Value.FileAddrStart;
                                            Byte[] bytes = reader.ReadBytes(data.Value.FileSize);
                                            if (title == "")
                                            {
                                                list.Remove(list.Last());
                                                targetPath = Path.Combine(directory, string.Join("\\",list));
                                                Console.WriteLine(Path.Combine(targetPath, targetFilePath));
                                                Common.SafeCreateDirectory(targetPath);
                                            }
                                            File.WriteAllBytes(Path.Combine(targetPath, targetFilePath), bytes);

                                            headerEndAddr = data.Value.FilePathAddrStart - 1;
                                        }
                                    }
                                }
                            }
                            break;
                        case @".XWB":
                            {
                                using( FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) )
                                {
                                    Console.WriteLine( "Reading XWB bank" );
                                    MicrosoftXWB bank = MicrosoftXWB.Read(fs);
                                    string outPath = Path.Combine( Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename) );

                                    string songId = Path.GetFileNameWithoutExtension(@filename);
                                    string title = songId;
                                    string outTitle = songId;
                                    string series = "";
                                    if (db[songId]["TITLE"] != "")
                                    {
                                        title = db[songId]["TITLE"];
                                        outTitle = title;
                                        series = db[songId]["series"];
                                    }
                                    outTitle = Common.nameReplace(outTitle);
                                    string targetPath = Path.Combine(directory, series, outTitle);
                                    Common.SafeCreateDirectory(targetPath);

                                    int count = bank.SoundCount;

                                    for( int i=0; i<count; i++ )
                                    {
                                        string outFileName;

                                        if( bank.Sounds[i].Name == null || bank.Sounds[i].Name == "" )
                                            outFileName = Util.ConvertToHexString(i, 4);
                                        else
                                            outFileName = bank.Sounds[i].Name;

                                        string outFile = Path.Combine(targetPath, outFileName + ".wav" );
                                        Console.WriteLine( "Writing " + outFile );
                                        bank.Sounds[i].WriteFile( outFile, 1.0f );
                                    }

                                    bank = null;
                                }
                            }
                            break;
                        case @".SSQ":
                            {
                                string title = "";
                                string artist = "";
                                string titleTranslit = "";
                                string artistTranslit = "";
                                int movieFlag = 0;
                                int movieOffset = 0;
                                string series = "";

                                using ( FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) )
                                {
                                    BemaniSSQ ssq = BemaniSSQ.Read(fs, 0x1000);
                                    StepmaniaSM sm = new StepmaniaSM();

                                    string songId = Path.GetFileNameWithoutExtension(@filename);

                                    if (db[songId]["TITLE"] != "")
                                    {
                                        title = db[songId]["TITLE"];
                                        artist = db[songId]["ARTIST"];
                                        movieFlag = db[songId].GetValue("MOVIE");
                                        movieOffset = db[songId].GetValue("MOVIEOFFSET");
                                        series = db[songId]["series"];
                                    }

                                    sm.Tags["SongID"] = songId;
                                    sm.Tags["TITLE"] = title;
                                    sm.Tags["ARTIST"] = artist;
                                    if( titleTranslit != "" )
                                        sm.Tags["TITLETRANSLIT"] = titleTranslit;
                                    if( artistTranslit != "" )
                                        sm.Tags["ARTISTTRANSLIT"] = artistTranslit;

                                    if (movieFlag > 0) { }
                                        sm.Tags["BGCHANGES"] = ((float)movieOffset / 1000) + "=" + songId + ".mpg=1.000=1=1=0";

                                    sm.Tags["BANNER"] = songId + "_jk.png";
                                    sm.Tags["MUSIC"] = songId + ".wav";
                                    sm.Tags["PREVIEW"] = songId + "_s.wav";

                                    sm.CreateTempoTags( ssq.TempoEntries.ToArray() );

                                    string[] gType = { "dance-single", "dance-double", "dance-couple", "dance-solo" };
                                    string meter = "";

                                    foreach( string gName in gType )
                                    {
                                        string[] dType = { "Beginner", "Easy", "Medium", "Hard", "Challenge", "" };

                                        foreach( string dName in dType )
                                        {
                                            foreach( Chart chart in ssq.Charts )
                                            {
                                                string gameType = config["SM"]["DanceMode" + chart.Tags["Panels"]];
                                                
                                                if( gName == gameType )
                                                {
                                                    string difficulty = config["SM"]["Difficulty" + config["DDR"]["Difficulty" + chart.Tags["Difficulty"]]];
                                                    chart.Entries.Sort();

                                                    if( gameType == config["SM"]["DanceMode8"] && difficulty == "" )
                                                        break;

                                                    if( dName == difficulty )
                                                    {
                                                        // solo chart check
                                                        if( gameType == config["SM"]["DanceMode6"] )
                                                        {
                                                            foreach( Entry entry in chart.Entries )
                                                            {
                                                                if( entry.Type == EntryType.Marker )
                                                                {
                                                                    switch( entry.Column )
                                                                    {
                                                                        case 0: entry.Column = 0; break;
                                                                        case 1: entry.Column = 2; break;
                                                                        case 2: entry.Column = 3; break;
                                                                        case 3: entry.Column = 5; break;
                                                                        case 4: entry.Column = 1; break;
                                                                        case 6: entry.Column = 4; break;
                                                                    }
                                                                }
                                                            }
                                                        }

                                                        // couples chart check
                                                        else if( gameType == config["SM"]["DanceMode4"] )
                                                        {
                                                            foreach( Entry entry in chart.Entries )
                                                            {
                                                                if( entry.Type == EntryType.Marker && entry.Column >= 4 )
                                                                {
                                                                    gameType = config["SM"]["DanceModeCouple"];
                                                                    chart.Tags["Panels"] = "8";
                                                                    break;
                                                                }
                                                            }
                                                        }

                                                        string difText = difficulty;

                                                        switch( difficulty )
                                                        {
                                                            case "Easy": difText = "Basic"; break;
                                                            case "Medium": difText = "Difficult"; break;
                                                            case "Hard": difText = "Expert"; break;
                                                            case "": difText = "Difficult"; break;
                                                        }

                                                        if (db[songId]["TITLE"] != "")
                                                        {
                                                            int player = 1;
                                                            switch (chart.Tags["Panels"])
                                                            {
                                                                case "4": player = 1; break;
                                                                case "6": player = 1; break;
                                                                case "8": player = 3; break;
                                                                default: player = 3; break;
                                                            }
                                                            meter = db[sm.Tags["SongID"]]["DIFFLV" + player + config["DDR"]["Difficulty" + chart.Tags["Difficulty"]]];
                                                        }

                                                        if( meter == "" )
                                                            meter = "0";

                                                        string dif = difficulty;

                                                        if( difficulty == "" )
                                                            dif = "Medium";

                                                        sm.CreateStepTag( chart.Entries.ToArray(), gameType, "", dif, meter, "", System.Convert.ToInt32(chart.Tags["Panels"]), config["SM"].GetValue( "QuantizeNotes" ) );
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    string outTitle = title;

                                    if( titleTranslit != "" )
                                        outTitle = titleTranslit;
                                    else if( title == "" )
                                        outTitle = Path.GetFileNameWithoutExtension(@filename);

                                    outTitle = Common.nameReplace(outTitle);
                                    string targetPath = Path.Combine(directory, series, outTitle);
                                    Common.SafeCreateDirectory(targetPath);

                                    // Move the video if it exists
                                    if (movieFlag > 0 && movieFolder != "")
                                    {
                                        string moviePath = Path.Combine(movieFolder, songId + ".m2v");
                                        if (!File.Exists(moviePath))
                                            moviePath = Path.Combine(movieFolder, songId + "_w.m2v");
                                        FileInfo file = new FileInfo(moviePath);

                                        string copyPath = Path.Combine(targetPath, songId + ".mpg");
                                        file.CopyTo(copyPath);
                                    }

                                    sm.WriteFile( Path.Combine(targetPath, outTitle + ".sm") );
                                }
                            }
                            break;
                    }
                }
            }
        }

        static private string ToUpperFirstLetter( this string source )
        {
            if( string.IsNullOrEmpty(source) )
                return string.Empty;
            // convert to char array of the string
            char[] letters = source.ToCharArray();
            // upper case the first char
            letters[0] = char.ToUpper(letters[0]);
            // return the array made of the new char array
            return new string(letters);
        }

        static private Configuration LoadDB()
        {
            Configuration config = Configuration.ReadFile(databaseFileName, "xml");
            return config;
        }
    }
}
