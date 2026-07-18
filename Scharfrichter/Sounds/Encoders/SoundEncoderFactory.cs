using System;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public static class SoundEncoderFactory
    {
        public const string DefaultFormat = "ogg";

        public static ISoundEncoder Create(string format)
        {
            switch (NormalizeFormat(format))
            {
                case "ogg":
                    return new OggEncoder();
                case "flac":
                    return new FlacEncoder();
                case "wav":
                    return new WavEncoder();
                case "mp3":
                    return new Mp3Encoder();
                default:
                    throw CreateUnsupportedFormatException(format);
            }
        }

        public static string NormalizeFormat(string format)
        {
            if (String.IsNullOrWhiteSpace(format))
                return DefaultFormat;

            string normalized = format.Trim().TrimStart('.').ToLowerInvariant();
            if (normalized == "wave")
                return "wav";

            return normalized;
        }

        public static string GetFileExtension(string format)
        {
            return NormalizeFormat(format);
        }

        public static NotSupportedException CreateUnsupportedFormatException(string format)
        {
            return new NotSupportedException("Sound output format " + format + " is not supported. Supported formats: ogg, flac, wav, mp3.");
        }
    }
}
