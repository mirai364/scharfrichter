using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Scharfrichter.Common
{
    public partial class Configuration
    {
        static public Configuration LoadDDRConfig(string configFileName)
        {
            Configuration config = Configuration.ReadFile(configFileName);
            config["SM"].SetDefaultValue( "QuantizeNotes", 192 );
            config["SM"].SetDefaultString( "DanceMode4", "dance-single" );
            config["SM"].SetDefaultString( "DanceMode6", "dance-solo" );
            config["SM"].SetDefaultString( "DanceMode8", "dance-double" );
            config["SM"].SetDefaultString( "DanceModeCouple", "dance-couple" );
            config["SM"].SetDefaultString( "Difficulty0", "Challenge" );
            config["SM"].SetDefaultString( "Difficulty1", "Easy" );
            config["SM"].SetDefaultString( "Difficulty2", "Medium" );
            config["SM"].SetDefaultString( "Difficulty3", "Hard" );
            config["SM"].SetDefaultString( "Difficulty4", "Beginner" );
            config["SM"].SetDefaultString( "Difficulty5", "Edit" );
            config["DDR"].SetDefaultString( "Difficulty1", "1" );
            config["DDR"].SetDefaultString( "Difficulty2", "2" );
            config["DDR"].SetDefaultString( "Difficulty3", "3" );
            config["DDR"].SetDefaultString( "Difficulty4", "4" );
            config["DDR"].SetDefaultString( "Difficulty6", "0" );
            return config;
        }

    }
}
