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

            byte[] finalData = ShouldRender(sound, masterVolume) ? sound.Render(masterVolume) : sound.Data;

            VorbisInfo info = VorbisInfo.InitVariableBitRate(sound.Format.Channels, sound.Format.SampleRate, _baseQuality);

            int serialNo = new Random().Next();
            OggStream oggStream = new OggStream(serialNo);

            ProcessingState processingState = ProcessingState.Create(info);

            // Disable PreExtrapolate via reflection to prevent the Vorbis encoder
            // from overwriting the head padding with backward-extrapolated audio.
            // PreExtrapolate fills the first blockSize[1]/2 buffer positions
            // with LPC-predicted values from subsequent real audio, which causes
            // the decoded waveform to shift progressively earlier by up to 23ms.
            var preExtrapolatedField = typeof(ProcessingState).GetField("_preExtrapolated",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (preExtrapolatedField != null)
                preExtrapolatedField.SetValue(processingState, true);

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
            int channelCount = sound.Format.Channels;
            if (channelCount <= 0)
                return;

            int bytesPerFrame = bytesPerSample * channelCount;
            int usableByteCount = finalData.Length - (finalData.Length % bytesPerFrame);
            const int samplesPerChannelPerChunk = 16384;

            int codecBlockSize = info.CodecSetup.BlockSizes[1];
            int paddingSamples = codecBlockSize / 2;

            // Prepend paddingSamples of silence at the head to prevent the Vorbis
            // encoder's PreExtrapolate() from consuming real audio samples.
            // PreExtrapolate fills the first paddingSamples positions of the
            // internal buffer with backward-extrapolated values from subsequent
            // audio. Without head padding, the decoder's first paddingSamples
            // output samples are synthetic extrapolations that shift the real
            // audio earlier by ~23ms relative to the original WAV.
            float[][] silenceBuffer = new float[channelCount][];
            for (int c = 0; c < channelCount; c++)
                silenceBuffer[c] = new float[paddingSamples];
            processingState.WriteData(silenceBuffer, paddingSamples);
            FlushPackets(processingState, oggStream, target, false);

            float[][] floatSamples = new float[channelCount][];
            for (int c = 0; c < channelCount; c++)
                floatSamples[c] = new float[samplesPerChannelPerChunk];

            for (int byteOffset = 0; byteOffset < usableByteCount;)
            {
                int chunkByteCount = Math.Min(samplesPerChannelPerChunk * bytesPerFrame, usableByteCount - byteOffset);
                int chunkSamplesPerChannel = chunkByteCount / bytesPerFrame;

                for (int sampleIndex = 0; sampleIndex < chunkSamplesPerChannel; sampleIndex++)
                {
                    int frameOffset = byteOffset + (sampleIndex * bytesPerFrame);
                    for (int channel = 0; channel < channelCount; channel++)
                    {
                        short sample16 = BitConverter.ToInt16(finalData, frameOffset + (channel * bytesPerSample));
                        floatSamples[channel][sampleIndex] = sample16 / 32768f;
                    }
                }

                processingState.WriteData(floatSamples, chunkSamplesPerChannel);
                FlushPackets(processingState, oggStream, target, false);
                byteOffset += chunkByteCount;
            }

            // Append paddingSamples of silence at the tail to prevent the Vorbis
            // lapped MDCT transform from truncating the end of the audio.
            // Without this, the encoder's granule position will be short by
            // exactly paddingSamples, causing the OGG to end earlier than the WAV.
            for (int c = 0; c < channelCount; c++)
                silenceBuffer[c] = new float[paddingSamples];
            processingState.WriteData(silenceBuffer, paddingSamples);
            FlushPackets(processingState, oggStream, target, false);

            // Process the end of the stream (sets the EOS flag)
            processingState.WriteEndOfStream();
            FlushPackets(processingState, oggStream, target, false);

            // Force flush all remaining pages in the Ogg container
            FlushPages(oggStream, target, true);
        }

        private bool ShouldRender(Sound sound, float masterVolume)
        {
            return masterVolume != 1.0f ||
                   sound.Volume != 1.0f ||
                   sound.Panning != 0.5f ||
                   sound.VolumeIsLinear ||
                   sound.PanningIsLinear;
        }

        private void FlushPackets(ProcessingState processingState, OggStream oggStream, Stream target, bool forcePages)
        {
            while (processingState.PacketOut(out OggPacket packet))
            {
                oggStream.PacketIn(packet);
                FlushPages(oggStream, target, forcePages);
            }
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