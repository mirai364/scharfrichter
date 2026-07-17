using NAudio.Utils;
using NAudio.Wave;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds
{
    public class Sound
    {
        public int Channel = -1;
        public byte[] Data { get; set; }
        public WaveFormat Format { get; set; }
        public string Name = "";
        public float Panning = 0.5f;
        public bool PanningIsLinear = false;
        public float Volume = 1.0f;
        public bool VolumeIsLinear = false;

        public Sound()
        {
            Data = new byte[] { };
            Format = null;
        }

        public Sound(byte[] newData, WaveFormat newFormat)
        {
            SetSound(newData, newFormat);
        }

        public static Sound Read(Stream source)
        {
            Sound result = new Sound();
            using (WaveFileReader reader = new WaveFileReader(source))
            {
                if (reader.Length > 0)
                {
                    result.Data = new byte[reader.Length];
                    reader.ReadExactly(result.Data, 0, result.Data.Length);
                    result.Format = reader.WaveFormat;
                }
                else
                {
                    result.Data = new byte[] { };
                    result.Format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, 44100, 2, 44100 * 4, 4, 16);
                }
            }
            result.Panning = 0.5f;
            result.Volume = 1.0f;
            return result;
        }

        public byte[] Render(float masterVolume)
        {
            try
            {
                using (MemoryStream sourceLeft = new MemoryStream(Data))
                using (MemoryStream sourceRight = new MemoryStream(Data))
                using (RawSourceWaveStream waveLeft = new RawSourceWaveStream(new IgnoreDisposeStream(sourceLeft), Format))
                using (RawSourceWaveStream waveRight = new RawSourceWaveStream(new IgnoreDisposeStream(sourceRight), Format))
                {
                    // Step 1: Separate the stereo stream
                    MultiplexingWaveProvider demuxLeft = new MultiplexingWaveProvider(new IWaveProvider[] { waveLeft }, 1);
                    MultiplexingWaveProvider demuxRight = new MultiplexingWaveProvider(new IWaveProvider[] { waveRight }, 1);
                    demuxLeft.ConnectInputToOutput(0, 0);
                    demuxRight.ConnectInputToOutput(1, 0);

                    // Step 2: Adjust the volume of a stereo stream
                    VolumeWaveProvider16 volLeft = new VolumeWaveProvider16(demuxLeft);
                    VolumeWaveProvider16 volRight = new VolumeWaveProvider16(demuxRight);

                    float volumeValueLeft;
                    float volumeValueRight;

                    if (!PanningIsLinear)
                    {
                        // Log scale applied to each operation
                        volumeValueLeft = (float)Math.Pow(1.0f - Panning, 0.5f);
                        volumeValueRight = (float)Math.Pow(Panning, 0.5f);
                    }
                    else
                    {
                        volumeValueLeft = 1.0f - Panning;
                        volumeValueRight = Panning;
                    }

                    if (!VolumeIsLinear)
                    {
                        // Ensure 1:1 conversion
                        volumeValueLeft /= (float)Math.Sqrt(0.5);
                        volumeValueRight /= (float)Math.Sqrt(0.5);
                        // Apply volume
                        volumeValueLeft *= (float)Math.Pow(Volume, 0.5f);
                        volumeValueRight *= (float)Math.Pow(Volume, 0.5f);
                    }
                    else
                    {
                        volumeValueLeft *= Volume;
                        volumeValueRight *= Volume;
                    }

                    // Use linear scale for master volume
                    volumeValueLeft *= masterVolume;
                    volumeValueRight *= masterVolume;

                    // Clamp limits
                    volumeValueLeft = Math.Max(volumeValueLeft, 0.0f);
                    volumeValueRight = Math.Max(volumeValueRight, 0.0f);

                    // Assign final volume values
                    volLeft.Volume = volumeValueLeft;
                    volRight.Volume = volumeValueRight;

                    // Step 3: Combine channels
                    IWaveProvider[] tracks = new IWaveProvider[] { volLeft, volRight };
                    MultiplexingWaveProvider mux = new MultiplexingWaveProvider(tracks, 2);

                    // Step 4: Export to byte array
                    byte[] finalData = new byte[Data.Length];
                    mux.Read(finalData, 0, finalData.Length);

                    return finalData;
                }
            }
            catch
            {
                return Data;
            }
        }

        public void SetSound(byte[] data, WaveFormat sourceFormat)
        {
            using (MemoryStream dataStream = new MemoryStream(data))
            using (RawSourceWaveStream wavStream = new RawSourceWaveStream(dataStream, sourceFormat))
            {
                WaveStream wavConvertStream = null;
                try
                {
                    wavConvertStream = WaveFormatConversionStream.CreatePcmStream(wavStream);

                    // Force all sounds to 2 channels
                    MultiplexingWaveProvider sourceProvider = new MultiplexingWaveProvider(new IWaveProvider[] { wavConvertStream }, 2);
                    int bytesToRead = (int)((wavConvertStream.Length * 2) / wavConvertStream.WaveFormat.Channels);
                    byte[] rawWaveData = new byte[bytesToRead];
                    sourceProvider.Read(rawWaveData, 0, bytesToRead);

                    Data = rawWaveData;
                    Format = sourceProvider.WaveFormat;
                }
                catch
                {
                    Data = data;
                    Format = sourceFormat;
                }
                finally
                {
                    wavConvertStream?.Dispose();
                }
            }
        }

        public byte[] RenderNewFormat(float masterVolume, WaveFormat newFormat)
        {
            if (Data != null && Data.Length > 0)
            {
                using (MemoryStream dataStream = new MemoryStream(Render(masterVolume)))
                using (RawSourceWaveStream wavStream = new RawSourceWaveStream(dataStream, Format))
                using (MediaFoundationResampler resampler = new MediaFoundationResampler(wavStream, newFormat))
                {
                    double c1 = (double)resampler.WaveFormat.SampleRate / wavStream.WaveFormat.SampleRate;
                    double c2 = (double)resampler.WaveFormat.BitsPerSample / wavStream.WaveFormat.BitsPerSample;
                    double c3 = (double)resampler.WaveFormat.Channels / wavStream.WaveFormat.Channels;

                    byte[] buffer = new byte[(int)(wavStream.Length * (c1 * c2 * c3))];
                    resampler.Read(buffer, 0, buffer.Length);
                    return buffer;
                }
            }
            return Data;
        }
    }
}