using OggVorbisEncoder;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public class OggEncoder : ISoundEncoder
    {
        private readonly float _baseQuality;

        // Base quality parameter defaults to 0.2f, but can be injected via constructor
        public OggEncoder(float baseQuality = 0.2f)
        {
            _baseQuality = baseQuality;
        }

        public void EncodeToFile(Sound sound, string targetFile, float masterVolume)
        {
            using (FileStream target = new FileStream(targetFile, FileMode.Create, FileAccess.Write))
            {
                Encode(sound, target, masterVolume);
                target.Flush();
            }
        }

        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            byte[] finalData = sound.Render(masterVolume);

            VorbisInfo info = VorbisInfo.InitVariableBitRate(sound.Format.Channels, sound.Format.SampleRate, _baseQuality);

            int serialNo = new Random().Next();
            OggStream oggStream = new OggStream(serialNo);

            ProcessingState processingState = ProcessingState.Create(info);

            var comments = new Comments();
            //comments.AddTag("ENCODER", "Scharfrichter");

            var infoPacket = HeaderPacketBuilder.BuildInfoPacket(info);
            var commentsPacket = HeaderPacketBuilder.BuildCommentsPacket(comments);
            var booksPacket = HeaderPacketBuilder.BuildBooksPacket(info);

            oggStream.PacketIn(infoPacket);
            oggStream.PacketIn(commentsPacket);
            oggStream.PacketIn(booksPacket);

            FlushPages(oggStream, target, true);

            int bytesPerSample = 2; // 16-bit
            int sampleCount = finalData.Length / bytesPerSample;
            int samplesPerChannel = sampleCount / sound.Format.Channels;

            float[][] floatSamples = new float[sound.Format.Channels][];
            for (int c = 0; c < sound.Format.Channels; c++)
            {
                floatSamples[c] = new float[samplesPerChannel];
            }

            for (int i = 0; i < sampleCount; i++)
            {
                short sample16 = BitConverter.ToInt16(finalData, i * bytesPerSample);
                float sampleFloat = sample16 / 32768f;

                int channel = i % sound.Format.Channels;
                int sampleIndex = i / sound.Format.Channels;

                floatSamples[channel][sampleIndex] = sampleFloat;
            }

            // Encode and write the data
            processingState.WriteData(floatSamples, samplesPerChannel);

            // Extract currently prepared packets and write them to the stream
            while (processingState.PacketOut(out OggPacket packet))
            {
                oggStream.PacketIn(packet);
                FlushPages(oggStream, target, false);
            }

            // Process the end of the stream (sets the EOS flag)
            processingState.WriteEndOfStream();

            // Extract remaining final packets (this includes the EOS packet)
            while (processingState.PacketOut(out OggPacket finalPacket))
            {
                oggStream.PacketIn(finalPacket);
                FlushPages(oggStream, target, false);
            }

            // Force flush all remaining pages in the Ogg container
            FlushPages(oggStream, target, true);
        }

        private void FlushPages(OggStream oggStream, Stream outputStream, bool force)
        {
            while (oggStream.PageOut(out OggPage page, force))
            {
                outputStream.Write(page.Header, 0, page.Header.Length);
                outputStream.Write(page.Body, 0, page.Body.Length);
            }
        }
    }
}