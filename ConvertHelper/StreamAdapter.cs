using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Media;
using System.IO;

namespace ConvertHelper
{
    /// <summary>
    /// Describes an opened source stream and the logical length consumers should use.
    /// </summary>
    public class StreamAdapterInfo
    {
        public long Length;
        public Stream Stream;

        /// <summary>
        /// Creates stream metadata using the stream length.
        /// </summary>
        public StreamAdapterInfo(Stream stream)
            : this(stream, stream.Length)
        {
        }

        /// <summary>
        /// Creates stream metadata with an explicit logical length.
        /// </summary>
        public StreamAdapterInfo(Stream stream, long length)
        {
            Length = length;
            Stream = stream;
        }
    }

    /// <summary>
    /// Opens regular and container-compressed inputs behind a common stream interface.
    /// </summary>
    static public class StreamAdapter
    {
        /// <summary>
        /// Opens a file, expanding supported container formats when needed.
        /// </summary>
        static public StreamAdapterInfo Open(string filename)
        {
            filename = filename.ToLowerInvariant().Trim();
            if (!File.Exists(filename))
                return null;

            if (filename.EndsWith(@".chd"))
                return OpenChd(filename);

            if (filename.EndsWith(@".gz"))
                return OpenGzip(filename);

            if (filename.EndsWith(@".zip"))
                return OpenZip(filename);

            return OpenRegularFile(filename);
        }

        /// <summary>
        /// Wraps an already-open stream in adapter metadata.
        /// </summary>
        static public StreamAdapterInfo Open(Stream source)
        {
            return new StreamAdapterInfo(source, source.Length);
        }

        /// <summary>
        /// Opens a CHD image stream.
        /// </summary>
        private static StreamAdapterInfo OpenChd(string filename)
        {
            return new StreamAdapterInfo(CHD.Load(new FileStream(filename, FileMode.Open, FileAccess.Read)));
        }

        /// <summary>
        /// Opens a gzip stream with a sentinel length for chunk arithmetic.
        /// </summary>
        private static StreamAdapterInfo OpenGzip(string filename)
        {
            var gz = new Gzip(filename);
            return new StreamAdapterInfo(gz.GetDeflateStream(), 0x7FFFFFFFFFFFL);
        }

        /// <summary>
        /// Opens the first file from a zip archive.
        /// </summary>
        private static StreamAdapterInfo OpenZip(string filename)
        {
            var zip = new Zip(filename);
            if (zip.Files.Count == 0)
                return null;

            var file = zip.Files[0];
            long length = GetZipEntryLength(file);
            return new StreamAdapterInfo(zip.StreamFile(file), length);
        }

        /// <summary>
        /// Chooses the best available logical length for a zip entry.
        /// </summary>
        private static long GetZipEntryLength(ZipDirectoryEntry file)
        {
            if (file.UncompressedSize != 0 || file.CompressedSize == 0)
                return file.UncompressedSize;

            return file.CompressedSize;
        }

        /// <summary>
        /// Opens a normal file stream.
        /// </summary>
        private static StreamAdapterInfo OpenRegularFile(string filename)
        {
            return new StreamAdapterInfo(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read));
        }
    }
}