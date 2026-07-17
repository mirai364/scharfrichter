using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using Image = SixLabors.ImageSharp.Image;

namespace DDSReader
{
    /// <summary>
    /// Loads DDS image data and saves it through ImageSharp-supported encoders.
    /// </summary>
    public class DDSImage
    {
        private readonly Pfim.IImage _image;

        /// <summary>
        /// Gets the decoded image bytes, or an empty array when no image is loaded.
        /// </summary>
        public byte[] Data
        {
            get
            {
                if (_image != null)
                    return _image.Data;
                return new byte[0];
            }
        }

        /// <summary>
        /// Loads a DDS image from a file path.
        /// </summary>
        public DDSImage(string file)
        {
            _image = Pfim.Pfimage.FromFile(file);
            Process();
        }

        /// <summary>
        /// Loads a DDS image from a stream.
        /// </summary>
        public DDSImage(Stream stream)
        {
            if (stream == null)
                throw new Exception("DDSImage ctor: Stream is null");

            _image = Pfim.Dds.Create(stream, new Pfim.PfimConfig());
            Process();
        }

        /// <summary>
        /// Loads a DDS image from an in-memory byte array.
        /// </summary>
        public DDSImage(byte[] data)
        {
            if (data == null || data.Length <= 0)
                throw new Exception("DDSImage ctor: no data");

            _image = Pfim.Dds.Create(data, new Pfim.PfimConfig());
            Process();
        }

        /// <summary>
        /// Saves the decoded image using the destination file extension to choose the encoder.
        /// </summary>
        public void Save(string file)
        {
            if (_image.Format == Pfim.ImageFormat.Rgba32)
            {
                var image = Image.LoadPixelData<Bgra32>(_image.Data, _image.Width, _image.Height);
                image.Save(file);
                return;
            }

            if (_image.Format == Pfim.ImageFormat.Rgb24)
            {
                var image = Image.LoadPixelData<Rgb24>(_image.Data, _image.Width, _image.Height);
                image.Save(file);
                return;
            }

            throw new Exception("Unsupported pixel format (" + _image.Format + ")");
        }

        /// <summary>
        /// Validates and decompresses the DDS image data when required.
        /// </summary>
        private void Process()
        {
            if (_image == null)
                throw new Exception("DDSImage image creation failed");

            if (_image.Compressed)
                _image.Decompress();
        }
    }
}