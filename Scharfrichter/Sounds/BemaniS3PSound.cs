using NAudio;
using NAudio.Codecs;
using NAudio.Wave;
using NAudio.WindowsMediaFormat;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scharfrichter.Codec.Sounds
{
    static public class BemaniS3PSound
    {
        static public Sound Read(byte[] source)
        {
            Sound result = new Sound();
            string fileName = "tmp.wma";

            File.WriteAllBytes(fileName, source);
            WMAFileReader fileReader = new WMAFileReader(fileName);
            File.Delete(fileName);
            using (WaveStream wavStream = WaveFormatConversionStream.CreatePcmStream(fileReader))
            {
                int bytesToRead;

                // using a mux, we force all sounds to be 2 channels
                bytesToRead = (int)wavStream.Length;

                byte[] rawWaveData = new byte[bytesToRead];
                int bytesRead = wavStream.Read(rawWaveData, 0, bytesToRead);
                result.SetSound(rawWaveData, wavStream.WaveFormat);
            }

            return result;
        }
    }
}
