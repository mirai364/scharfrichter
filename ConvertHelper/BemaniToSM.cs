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
    static public class BemaniToSM
    {
        private const string configFileName = "Convert";
        private const string databaseFileName = "musicdb";

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

            string iSelect = "";

            /*
            foreach( string filename in args )
            {
                if( File.Exists(filename) && Path.GetExtension(filename).ToUpper() == ".SSQ" )
                {
                    Console.WriteLine();
                    Console.Write( "At least one ssq files detected." );
                    Console.WriteLine();
                    Console.Write( "Enable manual fill-up simfile data?" );
                    Console.WriteLine();
                    Console.Write( "Input y for Yes, ENTER for No: ");
                    iSelect = Console.ReadLine();
                    break;
                }
            }
            */

            // process
            foreach( string filename in args )
            {
                if( File.Exists(filename) )
                {
                    Console.WriteLine();
                    Console.WriteLine( "Processing File: " + filename );

                    switch( Path.GetExtension(filename).ToUpper() )
                    {
                        case @".XWB":
                            {
                                using( FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) )
                                {
                                    Console.WriteLine( "Reading XWB bank" );
                                    MicrosoftXWB bank = MicrosoftXWB.Read(fs);
                                    string outPath = Path.Combine( Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename) );

                                    Directory.CreateDirectory( outPath );

                                    int count = bank.SoundCount;

                                    for( int i=0; i<count; i++ )
                                    {
                                        string outFileName;

                                        if( bank.Sounds[i].Name == null || bank.Sounds[i].Name == "" )
                                            outFileName = Util.ConvertToHexString(i, 4);
                                        else
                                            outFileName = bank.Sounds[i].Name;

                                        string outFile = Path.Combine( outPath, outFileName + ".wav" );
                                        Console.WriteLine( "Writing " + outFile );
                                        bank.Sounds[i].WriteFile( outFile, 1.0f );
                                    }

                                    bank = null;
                                }
                            }
                            break;
                        case @".SSQ":
                            {
                                string iTitle = "";
                                string iArtist = "";
                                string iTitleTranslit = "";
                                string iArtistTranslit = "";
                                string iCDTitle = "";
                                int iMovieFlag = 0;
                                int iMovieOffset = 0;

                                using ( FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) )
                                {
                                    BemaniSSQ ssq = BemaniSSQ.Read(fs, 0x1000);
                                    StepmaniaSM sm = new StepmaniaSM();

                                    string songId = Path.GetFileNameWithoutExtension(@filename);

                                    if ( iSelect == "y" )
                                    {
                                        Console.WriteLine();
                                        Console.Write("TITLE: ");
                                        iTitle = Console.ReadLine();

                                        Console.Write("ARTIST: ");
                                        iArtist = Console.ReadLine();

                                        Console.Write("TITLETRANSLIT: ");
                                        iTitleTranslit = Console.ReadLine();

                                        Console.Write("ARTISTTRANSLIT: ");
                                        iArtistTranslit = Console.ReadLine();

                                        Console.Write("Origin (for CDTitle): ");
                                        iCDTitle = Console.ReadLine();

                                        Console.WriteLine();
                                        Console.WriteLine("Input difficulty ratings for song " + iTitle + " below.");
                                        Console.WriteLine();
                                    } else if (db[songId]["TITLE"] != "")
                                    {
                                        iTitle = db[songId]["TITLE"];
                                        iArtist = db[songId]["ARTIST"];
                                        iMovieFlag = db[songId].GetValue("MOVIE");
                                        iMovieOffset = db[songId].GetValue("MOVIEOFFSET");
                                        //iTitleTranslit = db[songId]["TITLE"];
                                        //iArtistTranslit = db[songId]["ARTIST"];
                                        Console.WriteLine();
                                        Console.WriteLine("Input difficulty ratings for song " + iTitle + " below.");
                                        Console.WriteLine();
                                    }

                                    sm.Tags["SongID"] = songId;
                                    sm.Tags["TITLE"] = iTitle;
                                    sm.Tags["ARTIST"] = iArtist;
                                    if( iTitleTranslit != "" )
                                        sm.Tags["TITLETRANSLIT"] = iTitleTranslit;
                                    if( iArtistTranslit != "" )
                                        sm.Tags["ARTISTTRANSLIT"] = iArtistTranslit;

                                    if (iMovieFlag > 0)
                                    {
                                        sm.Tags["BGCHANGES"] = ((float)iMovieOffset / 1000) + "=" + songId + ".mpg=1.000=1=1=0";
                                    }
                                    /*
                                    if( iTitleTranslit == "" )
                                        sm.Tags["BANNER"] = iTitle + ".png";
                                    else
                                        sm.Tags["BANNER"] = iTitleTranslit + ".png";

                                    if( iTitleTranslit == "" )
                                        sm.Tags["BACKGROUND"] = iTitle + "-bg.png";
                                    else
                                        sm.Tags["BACKGROUND"] = iTitleTranslit + "-bg.png";

                                    sm.Tags["CDTITLE"] = "./CDTitles/" + iCDTitle + ".png";

                                    if( iTitleTranslit == "" )
                                        sm.Tags["MUSIC"] = iTitle + ".ogg";
                                    else
                                        sm.Tags["MUSIC"] = iTitleTranslit + ".ogg";
                                    */
                                    sm.Tags["MUSIC"] = songId + ".wav";
                                    sm.Tags["PREVIEW"] = songId + "_s.wav";

                                    //sm.Tags["SAMPLESTART"] = "20";
                                    //sm.Tags["SAMPLELENGTH"] = "15";

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

                                                        if( iSelect == "y" )
                                                        {
                                                            Console.Write(ToUpperFirstLetter(gameType.Replace("dance-", "")) + "-" + difText + ": ");
                                                            meter = Console.ReadLine();
                                                        } else if (db[songId]["TITLE"] != "")
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
                                                            /*
                                                            Console.Write(ToUpperFirstLetter(gameType.Replace("dance-", "")) + "-" + difText + ": ");
                                                            Console.WriteLine(db[sm.Tags["SongID"]]["DIFFLV" + player + config["DDR"]["Difficulty" + chart.Tags["Difficulty"]]]);
                                                            */
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

                                    string outTitle = iTitle;

                                    if( iTitleTranslit != "" )
                                        outTitle = iTitleTranslit;
                                    else if( iTitle == "" )
                                        outTitle = Path.GetFileNameWithoutExtension(@filename);

                                    sm.WriteFile( Path.Combine(Path.GetDirectoryName(filename), outTitle + ".sm") );
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
