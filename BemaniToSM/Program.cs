using System.IO;

namespace BemaniToSM
{
    class Program
    {
        static void Main(string[] args)
        {
            if (System.Diagnostics.Debugger.IsAttached && args.Length == 0)
            {
                args = Directory.GetFiles(@"C:\Users\Tony\Desktop\ex\ssq");
                //args = new string[] { @"C:\Users\Tony\Desktop\ex\ssq\Card00024576.ssq" };
            }

            ConvertHelper.BemaniToSM.Convert(args);
        }
    }
}
