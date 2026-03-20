using System;

namespace Scharfrichter.Common
{
    // each default application should call Scharfrichter.Common.Splash.Show(appname)
    // just for a uniform display anyway.. this is not required for custom projects

    static public class Splash
    {
        static public void Show(string applicationName)
        {
            Console.WriteLine(applicationName);
            Console.WriteLine(@"Using modified NAudio - http://naudio.codeplex.com/");
        }
    }
}
