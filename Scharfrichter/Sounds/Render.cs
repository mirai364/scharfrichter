using NAudio.Utils;
using NAudio.Wave;
using OggVorbisEncoder;     // For OGG
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds.Encoders;
using System;
using System.Collections.Generic;
using System.IO;

namespace Scharfrichter.Codec.Sounds
{
    static public class ChartRenderer
    {
        static private long GetIdentifier(Entry entry)
        {
            long result = (long)entry.Column;
            result <<= 32;
            result |= (long)(entry.Player) & 0xFFFFFFFFL;

            return result;
        }

        static private void Paste(byte[] sourceRendered, ref int[] target, Fraction offset, Fraction cutoffFraction)
        {
            if (sourceRendered == null)
                return;

            int sourceLength = sourceRendered.Length;

            int desiredOffset = (int)(offset * new Fraction(88200, 1));
            int desiredLength = (sourceRendered.Length / 2) + (int)desiredOffset;
            int cutoff = (int)(cutoffFraction * new Fraction(88200, 1));

            if (cutoff >= 0 && desiredOffset + (sourceLength / 4) > cutoff)
            {
                sourceLength = (cutoff - desiredOffset) * 4;
            }

            if (target.Length < desiredLength)
                Array.Resize(ref target, desiredLength);

            Int32 sourceSampleL = 0;
            Int32 sourceSampleR = 0;
            int sourceIndex = 0;
            int targetIndex = desiredOffset;

            while (sourceIndex < sourceLength - 3)
            {
                sourceSampleL = sourceRendered[sourceIndex++];
                sourceSampleL |= (int)(sourceRendered[sourceIndex++]) << 8;
                sourceSampleL <<= 16;
                sourceSampleL >>= 16;
                sourceSampleR = sourceRendered[sourceIndex++];
                sourceSampleR |= (int)(sourceRendered[sourceIndex++]) << 8;
                sourceSampleR <<= 16;
                sourceSampleR >>= 16;
                target[targetIndex++] += sourceSampleL;
                target[targetIndex++] += sourceSampleR;
            }
        }

        /// <summary>
        /// Renders the waveform and returns pure interleaved PCM samples along with format information.
        /// </summary>
        static private (Int16[] samples, WaveFormat format) RenderRawSamples(Chart chart, Sound[] sounds)
        {
            Dictionary<long, Entry> lastNote = new Dictionary<long, Entry>();
            Dictionary<int, Fraction> noteCutoff = new Dictionary<int, Fraction>();
            Dictionary<int, byte[]> renderedSamples = new Dictionary<int, byte[]>();

            int[] buffer = new int[0];
            WaveFormat newFormat = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, 44100, 2, 44100 * 4, 4, 16);

            chart.Entries.Reverse();

            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type == EntryType.Sample)
                {
                    lastNote[GetIdentifier(entry)] = entry;
                }
                else if (entry.Type == EntryType.Marker)
                {
                    if (entry.Value.Numerator > 0)
                    {
                        byte[] soundData = null;
                        var soundIndex = (int)entry.Value - 1;
                        var sound = soundIndex < sounds.Length ? sounds[soundIndex] : null;

                        if (renderedSamples.ContainsKey(soundIndex))
                        {
                            soundData = renderedSamples[soundIndex];
                        }
                        else if (sound != null)
                        {
                            if (sound.Format.Equals(newFormat))
                            {
                                soundData = sound.Render(1.0f);
                            }
                            else
                            {
                                soundData = sound.RenderNewFormat(1.0f, newFormat);
                            }
                            renderedSamples[soundIndex] = soundData;
                        }

                        var cutoff = new Fraction(-1, 1);
                        if (sound != null && sound.Channel >= 0 && noteCutoff.ContainsKey(sound.Channel))
                        {
                            cutoff = noteCutoff[sound.Channel];
                        }
                        if (soundData != null)
                        {
                            Paste(soundData, ref buffer, entry.LinearOffset * chart.TickRate, cutoff * chart.TickRate);
                        }
                        if (sound != null && sound.Channel >= 0)
                            noteCutoff[sound.Channel] = entry.LinearOffset;
                    }
                }
            }

            chart.Entries.Reverse();

            int length = buffer.Length;
            Int16[] outputSamples = new Int16[length];
            int normalization = 1;

            for (int i = 0; i < length; i++)
            {
                // auto-normalize
                int currentSample = buffer[i] / normalization;
                while (currentSample > 32767 || currentSample < -32768)
                {
                    normalization++;
                    currentSample = buffer[i] / normalization;
                }
            }

            for (int i = 0; i < length; i++)
            {
                outputSamples[i] = (Int16)(buffer[i] / normalization);
            }

            return (outputSamples, newFormat);
        }

        /// <summary>
        /// Performs commonized encoding to the specified format ("ogg", "flac", "wav", "mp3").
        /// </summary>
        static public byte[] RenderAsFormat(Chart chart, Sound[] sounds, string format)
        {
            // 1. Retrieve the rendered raw PCM data
            var result = RenderRawSamples(chart, sounds);
            Int16[] samples = result.samples;
            WaveFormat waveFormat = result.format;

            string targetFormat = format.ToLowerInvariant();

            // 2. For OGG, pass the pure PCM directly to OggVorbisEncoder
            if (targetFormat == "ogg")
            {
                return EncodeToOgg(samples, waveFormat);
            }

            byte[] pcmData = new byte[samples.Length * sizeof(short)];
            Buffer.BlockCopy(samples, 0, pcmData, 0, pcmData.Length);

            if (targetFormat == "wav" || targetFormat == "wave")
                return EncodeToWav(samples, waveFormat);

            if (targetFormat == "flac" || targetFormat == "mp3")
                return EncodeSoundData(pcmData, waveFormat, targetFormat);

            throw new NotSupportedException($"Format {format} is not supported.");
        }

        private static byte[] EncodeToWav(Int16[] samples, WaveFormat waveFormat)
        {
            using (MemoryStream mem = new MemoryStream())
            {
                using (WaveFileWriter writer = new WaveFileWriter(new IgnoreDisposeStream(mem), waveFormat))
                {
                    writer.WriteSamples(samples, 0, samples.Length);
                }
                mem.Flush();
                return mem.ToArray();
            }
        }

        private static byte[] EncodeSoundData(byte[] pcmData, WaveFormat waveFormat, string targetFormat)
        {
            ISoundEncoder encoder = targetFormat == "flac" ? (ISoundEncoder)new FlacEncoder() : new Mp3Encoder();
            Sound sound = new Sound(pcmData, waveFormat);
            using (MemoryStream output = new MemoryStream())
            {
                encoder.Encode(sound, output, 1.0f);
                return output.ToArray();
            }
        }        /// <summary>
        /// Encodes pure PCM data to OGG Vorbis using OggVorbisEncoder.
        /// </summary>
        static private byte[] EncodeToOgg(Int16[] pcmSamples, WaveFormat format)
        {
            int numChannels = format.Channels;
            int numFrames = pcmSamples.Length / numChannels;

            // OggVorbisEncoder requires independent float arrays for each channel
            float[][] floatSamples = new float[numChannels][];
            for (int c = 0; c < numChannels; c++)
            {
                floatSamples[c] = new float[numFrames];
            }

            // De-interleave and normalize from 16-bit integer to [-1.0f, 1.0f]
            for (int i = 0; i < numFrames; i++)
            {
                for (int c = 0; c < numChannels; c++)
                {
                    floatSamples[c][i] = pcmSamples[i * numChannels + c] / 32768f;
                }
            }

            // Initialize with VBR quality 0.8f (high quality setting, approx 256kbps)
            var info = VorbisInfo.InitVariableBitRate(numChannels, format.SampleRate, 0.8f);
            var serialNo = new Random().Next();
            var oggStream = new OggStream(serialNo);

            var comments = new Comments();
            comments.AddTag("ENCODER", "Scharfrichter");

            // FIX: HeaderPacketBuilder is a static class in recent versions.
            // Call the static methods directly instead of creating an instance.
            oggStream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
            oggStream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
            oggStream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));

            using (var outputStream = new MemoryStream())
            {
                FlushPages(oggStream, outputStream, false);

                var processingState = ProcessingState.Create(info);
                int writeBufferSize = 1024;

                for (int readIndex = 0; readIndex < numFrames; readIndex += writeBufferSize)
                {
                    int count = Math.Min(writeBufferSize, numFrames - readIndex);
                    processingState.WriteData(floatSamples, count, readIndex);

                    while (!oggStream.Finished && processingState.PacketOut(out OggPacket packet))
                    {
                        oggStream.PacketIn(packet);
                        FlushPages(oggStream, outputStream, false);
                    }
                }

                // End of stream processing
                processingState.WriteEndOfStream();

                while (!oggStream.Finished && processingState.PacketOut(out OggPacket packet))
                {
                    oggStream.PacketIn(packet);
                    FlushPages(oggStream, outputStream, false);
                }

                FlushPages(oggStream, outputStream, true);

                return outputStream.ToArray();
            }
        }

        static private void FlushPages(OggStream stream, MemoryStream output, bool force)
        {
            while (stream.PageOut(out OggPage page, force))
            {
                output.Write(page.Header, 0, page.Header.Length);
                output.Write(page.Body, 0, page.Body.Length);
            }
        }
    }
}