using System.IO;
using System.Text.RegularExpressions;

namespace Scharfrichter.Common
{
    public partial class Common
    {
        public const string configFileName = "Convert";

        static public Configuration LoadDB(string databaseFileName = "BeatmaniaDB")
        {
            Configuration config = Configuration.ReadFile(databaseFileName);
            return config;
        }

        /// <summary>
        /// replace prohibited characters for windows systems
        /// </summary>
        /// <param name="nameInfo"></param>
        /// <returns></returns>
        static public string nameReplace(string nameInfo)
        {
            nameInfo = nameInfo.Replace(":", "：");
            nameInfo = nameInfo.Replace("/", "_");
            nameInfo = nameInfo.Replace("?", "_");
            nameInfo = nameInfo.Replace("\\", "_");
            nameInfo = nameInfo.Replace("\"", "_");
            nameInfo = nameInfo.Replace("*", "_");
            nameInfo = nameInfo.Replace("|", "_");

            Regex reg = new Regex(@"\.\.\.$");
            nameInfo = reg.Replace(nameInfo, "…");
            reg = new Regex(@"\.\.$");
            nameInfo = reg.Replace(nameInfo, "_");
            reg = new Regex(@"\.$");
            nameInfo = reg.Replace(nameInfo, "_");
            return nameInfo;
        }

        /// <summary>
        /// Create folder if folder does not exist
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static DirectoryInfo SafeCreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                return null;
            }
            return Directory.CreateDirectory(path);
        }
    }
}
