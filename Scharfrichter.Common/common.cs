using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Scharfrichter.Common
{
    public partial class Common
    {
        public const string configFileName = "Convert";
        public const string databaseFileName = "BeatmaniaDB";

        static public Configuration LoadDB()
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
