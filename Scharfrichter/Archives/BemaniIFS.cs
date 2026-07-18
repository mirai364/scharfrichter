using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Scharfrichter.Codec.Archives
{
    public class BemaniIFS : Archive
    {
        static public readonly string PropBinaryNameChars =
            "0123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";

        public struct Header
        {
            public const int Size = 0x24;
            public const int Signature = 0x6CAD8F89;

            public int Identifier;
            public short Version;
            public short VersionXor;
            public int TimeStamp;
            public int ManifestMemorySize;
            public int BodyStart;
            public byte[] ManifestMD5;

            /// <summary>
            /// Reads the fixed-size IFS archive header from the current stream position.
            /// </summary>
            /// <param name="source">The stream positioned at the start of the IFS header.</param>
            /// <returns>The parsed archive header.</returns>
            static public Header Read(Stream source)
            {
                BinaryReaderEx reader = new BinaryReaderEx(source);
                Header result = new Header();
                result.Identifier = reader.ReadInt32S();
                result.Version = reader.ReadInt16S();
                result.VersionXor = reader.ReadInt16S();
                result.TimeStamp = reader.ReadInt32S();
                result.ManifestMemorySize = reader.ReadInt32S();
                result.BodyStart = reader.ReadInt32S();
                result.ManifestMD5 = reader.ReadBytes(16);
                return result;
            }
        }

        public class Entry
        {
            public string Name;
            public string Path;
            public int Offset;
            public int Size;
            public int TimeStamp;
            public byte[] Data;

            public string FullPath
            {
                get
                {
                    if (string.IsNullOrEmpty(Path))
                        return Name;
                    return System.IO.Path.Combine(Path, Name);
                }
            }
        }

        private List<byte[]> files = new List<byte[]>();
        private List<Entry> entries = new List<Entry>();
        private List<string> properties = new List<string>();

        public Entry[] Entries
        {
            get
            {
                return entries.ToArray();
            }
        }

        public string[] Properties
        {
            get
            {
                return properties.ToArray();
            }
            set
            {
                properties.Clear();
                properties.AddRange(value);
            }
        }

        public override byte[][] RawData
        {
            get
            {
                return files.ToArray();
            }
            set
            {
                files.Clear();
                files.AddRange(value);
            }
        }

        public override int RawDataCount
        {
            get
            {
                return files.Count;
            }
        }

        /// <summary>
        /// Reads an IFS archive, parses its binary XML manifest, and loads every file entry.
        /// </summary>
        /// <param name="source">The stream containing the complete IFS archive.</param>
        /// <returns>A populated IFS archive instance with raw data and named entries.</returns>
        static public BemaniIFS Read(Stream source)
        {
            BemaniIFS result = new BemaniIFS();

            Header header = Header.Read(source);
            if (header.Identifier != Header.Signature)
                throw new InvalidDataException("Given file was not an IFS file.");
            if ((short)(header.Version ^ header.VersionXor) != unchecked((short)0xFFFF))
                throw new InvalidDataException("IFS header version check failed.");
            if (header.BodyStart < Header.Size || header.BodyStart > source.Length)
                throw new InvalidDataException("IFS manifest length is invalid.");

            byte[] manifestData = new byte[header.BodyStart - Header.Size];
            source.ReadExactly(manifestData, 0, manifestData.Length);

            KBinElement manifest = KBinReader.Read(manifestData);
            foreach (Entry entry in CollectEntries(manifest, ""))
            {
                if (entry.Offset < 0 || entry.Size < 0 || header.BodyStart + entry.Offset + entry.Size > source.Length)
                    throw new InvalidDataException("IFS file entry points outside of the archive.");

                source.Position = header.BodyStart + entry.Offset;
                entry.Data = new byte[entry.Size];
                source.ReadExactly(entry.Data, 0, entry.Data.Length);
                result.entries.Add(entry);
                result.files.Add(entry.Data);
                result.properties.Add(entry.FullPath);
            }

            return result;
        }

        /// <summary>
        /// Recursively walks a manifest element and yields file entries with archive-relative paths.
        /// </summary>
        /// <param name="element">The manifest element to inspect.</param>
        /// <param name="path">The current folder path within the archive.</param>
        /// <returns>File entries discovered below the supplied manifest element.</returns>
        static private IEnumerable<Entry> CollectEntries(KBinElement element, string path)
        {
            foreach (KBinElement child in element.Children)
            {
                string name = FixName(child.Name);
                if (name == "_info_" || name == "_super_")
                    continue;

                int[] values = SplitInts(child.Text);
                bool isFile = (values.Length == 2 || values.Length == 3) && child.Children.Count == 0;
                if (isFile)
                {
                    yield return new Entry()
                    {
                        Name = name,
                        Path = path,
                        Offset = values[0],
                        Size = values[1],
                        TimeStamp = values.Length > 2 ? values[2] : -1,
                    };
                }
                else
                {
                    string nextPath = string.IsNullOrEmpty(path) ? name : System.IO.Path.Combine(path, name);
                    foreach (Entry entry in CollectEntries(child, nextPath))
                        yield return entry;
                }
            }
        }

        /// <summary>
        /// Parses a space-separated list of integers from a manifest text value.
        /// </summary>
        /// <param name="text">The manifest text to parse.</param>
        /// <returns>The parsed integer values, or an empty array when parsing fails.</returns>
        static private int[] SplitInts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new int[] { };

            string[] parts = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int[] result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out result[i]))
                    return new int[] { };
            }
            return result;
        }

        /// <summary>
        /// Converts an IFS-safe manifest element name back to its original file-system name.
        /// </summary>
        /// <param name="name">The sanitized manifest element name.</param>
        /// <returns>The restored file or folder name.</returns>
        static private string FixName(string name)
        {
            name = name.Replace("_E", ".");
            name = name.Replace("__", "_");
            if (name.Length > 1 && name[0] == '_' && char.IsDigit(name[1]))
                name = name.Substring(1);
            return name;
        }

        /// <summary>
        /// Converts binary XML data to UTF-8 XML text when the input uses the kbin format.
        /// </summary>
        /// <param name="data">The binary XML bytes to convert.</param>
        /// <param name="textData">The converted UTF-8 XML text bytes.</param>
        /// <returns>True when conversion succeeded; otherwise false.</returns>
        static public bool TryConvertBinaryXml(byte[] data, out byte[] textData)
        {
            textData = null;
            if (data == null || data.Length == 0 || data[0] != 0xA0)
                return false;

            try
            {
                KBinElement root = KBinReader.Read(data);
                XDocument document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root.ToXElement());
                textData = Encoding.UTF8.GetBytes(document.ToString() + Environment.NewLine);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private class KBinElement
        {
            public string Name;
            public string Text = "";
            public string TypeName;
            public Dictionary<string, string> Attributes = new Dictionary<string, string>();
            public List<KBinElement> Children = new List<KBinElement>();

            /// <summary>
            /// Converts this lightweight element into an XML element.
            /// </summary>
            /// <returns>The converted XML element.</returns>
            public XElement ToXElement()
            {
                XElement element = new XElement(Name);
                foreach (KeyValuePair<string, string> attribute in Attributes)
                    element.SetAttributeValue(attribute.Key, attribute.Value);
                if (!string.IsNullOrEmpty(TypeName))
                    element.SetAttributeValue("__type", TypeName);
                foreach (KBinElement child in Children)
                    element.Add(child.ToXElement());
                if (!string.IsNullOrEmpty(Text))
                    element.Value = Text;
                return element;
            }
        }

        private class KBinReader
        {
            private const byte Signature = 0xA0;
            private const byte Compressed = 0x42;
            private const byte Uncompressed = 0x45;
            private const int ArrayFlag = 0x40;
            private const int NodeStart = 1;
            private const int Binary = 10;
            private const int String = 11;
            private const int Attribute = 46;
            private const int NodeEnd = 190;
            private const int EndSection = 191;

            private static readonly string SixBitChars = PropBinaryNameChars;

            private readonly byte[] data;
            private readonly bool compressedNames;
            private int nodeOffset;
            private int nodeEnd;
            private int dataOffset;
            private int byteDataOffset;
            private int wordDataOffset;

            /// <summary>
            /// Initializes a binary XML reader and validates the manifest prologue.
            /// </summary>
            /// <param name="data">The raw binary XML manifest bytes.</param>
            private KBinReader(byte[] data)
            {
                this.data = data;
                nodeOffset = 0;

                if (ReadNodeByte() != Signature)
                    throw new InvalidDataException("IFS manifest is not binary XML.");

                byte compression = ReadNodeByte();
                if (compression != Compressed && compression != Uncompressed)
                    throw new InvalidDataException("Unsupported IFS manifest compression.");
                compressedNames = compression == Compressed;

                byte encoding = ReadNodeByte();
                if (ReadNodeByte() != (byte)(encoding ^ 0xFF))
                    throw new InvalidDataException("IFS manifest encoding check failed.");

                nodeEnd = ReadNodeInt32() + 8;
                dataOffset = nodeEnd + 4;
                byteDataOffset = nodeEnd;
                wordDataOffset = nodeEnd;
            }

            /// <summary>
            /// Parses a binary XML document into a lightweight element tree.
            /// </summary>
            /// <param name="data">The raw binary XML manifest bytes.</param>
            /// <returns>The root element of the parsed manifest.</returns>
            public static KBinElement Read(byte[] data)
            {
                KBinReader reader = new KBinReader(data);
                KBinElement sentinel = new KBinElement() { Name = "root" };
                Stack<KBinElement> stack = new Stack<KBinElement>();
                stack.Push(sentinel);

                while (reader.nodeOffset < reader.nodeEnd)
                {
                    while (reader.nodeOffset < reader.nodeEnd && reader.data[reader.nodeOffset] == 0)
                        reader.nodeOffset++;
                    if (reader.nodeOffset >= reader.nodeEnd)
                        break;

                    int nodeType = reader.ReadNodeByte();
                    bool isArray = (nodeType & ArrayFlag) != 0;
                    nodeType &= ~ArrayFlag;

                    string name = "";
                    if (nodeType != NodeEnd && nodeType != EndSection)
                        name = reader.ReadName();

                    if (nodeType == Attribute)
                    {
                        stack.Peek().Attributes[name] = reader.ReadStringData();
                        continue;
                    }
                    if (nodeType == NodeEnd)
                    {
                        if (stack.Count > 1)
                            stack.Pop();
                        continue;
                    }
                    if (nodeType == EndSection)
                        break;

                    KBinElement child = new KBinElement() { Name = name };
                    stack.Peek().Children.Add(child);
                    stack.Push(child);

                    if (nodeType == NodeStart)
                        continue;

                    child.TypeName = KBinFormat.Get(nodeType).GetTypeName(isArray);
                    child.Text = reader.ReadValue(nodeType, isArray);
                }

                if (sentinel.Children.Count == 0)
                    throw new InvalidDataException("IFS manifest does not contain an XML root.");
                return sentinel.Children[0];
            }

            /// <summary>
            /// Reads and formats the data payload for a typed binary XML node.
            /// </summary>
            /// <param name="nodeType">The binary XML node type without array flags.</param>
            /// <param name="isArray">Whether the node stores an array payload.</param>
            /// <returns>The node value formatted as manifest text.</returns>
            private string ReadValue(int nodeType, bool isArray)
            {
                KBinFormat format = KBinFormat.Get(nodeType);
                int count = format.Count;
                int arrayCount = 1;

                if (count == -1)
                {
                    count = ReadDataInt32();
                    isArray = true;
                }
                else if (isArray)
                {
                    int byteCount = ReadDataInt32();
                    arrayCount = byteCount / (format.Size * count);
                }

                int totalCount = arrayCount * count;
                byte[] valueData;
                if (isArray)
                {
                    valueData = ReadDataBytes(format.Size * totalCount);
                    AlignData();
                }
                else
                {
                    valueData = ReadAlignedData(format.Size * totalCount);
                }

                if (nodeType == Binary)
                    return ToHex(valueData);
                if (nodeType == String)
                    return Encoding.UTF8.GetString(valueData).TrimEnd('\0');

                return format.ToText(valueData, totalCount);
            }

            /// <summary>
            /// Reads a length-prefixed UTF-8 string from the binary XML data section.
            /// </summary>
            /// <returns>The decoded string without trailing null padding.</returns>
            private string ReadStringData()
            {
                int length = ReadDataInt32();
                byte[] bytes = ReadDataBytes(length);
                AlignData();
                return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            }

            /// <summary>
            /// Reads a scalar value using the byte, word, or main aligned data cursor.
            /// </summary>
            /// <param name="size">The number of bytes to read.</param>
            /// <returns>The requested bytes from the aligned data stream.</returns>
            private byte[] ReadAlignedData(int size)
            {
                if (byteDataOffset % 4 == 0)
                    byteDataOffset = dataOffset;
                if (wordDataOffset % 4 == 0)
                    wordDataOffset = dataOffset;

                byte[] result;
                if (size == 1)
                    result = ReadBytesAt(ref byteDataOffset, size);
                else if (size == 2)
                    result = ReadBytesAt(ref wordDataOffset, size);
                else
                    result = ReadDataBytes(size);

                AlignData();
                int trailing = Math.Max(byteDataOffset, wordDataOffset);
                if (dataOffset < trailing)
                    dataOffset = trailing;
                AlignData();
                return result;
            }

            /// <summary>
            /// Reads a node name from either compressed six-bit or plain UTF-8 name storage.
            /// </summary>
            /// <returns>The decoded node name.</returns>
            private string ReadName()
            {
                if (!compressedNames)
                {
                    int length = (ReadNodeByte() & ~ArrayFlag) + 1;
                    return Encoding.UTF8.GetString(ReadNodeBytes(length));
                }

                int lengthChars = ReadNodeByte();
                int lengthBits = lengthChars * 6;
                int lengthBytes = (lengthBits + 7) / 8;
                byte[] bytes = ReadNodeBytes(lengthBytes);
                char[] chars = new char[lengthChars];
                for (int i = 0; i < lengthChars; i++)
                {
                    int value = 0;
                    int bitOffset = i * 6;
                    for (int bit = 0; bit < 6; bit++)
                    {
                        int sourceBit = bitOffset + bit;
                        int sourceByte = sourceBit / 8;
                        int sourceMask = 0x80 >> (sourceBit % 8);
                        value <<= 1;
                        if ((bytes[sourceByte] & sourceMask) != 0)
                            value |= 1;
                    }
                    chars[i] = SixBitChars[value];
                }
                return new string(chars);
            }

            /// <summary>
            /// Reads one byte from the binary XML node stream.
            /// </summary>
            /// <returns>The next node-stream byte.</returns>
            private byte ReadNodeByte()
            {
                return data[nodeOffset++];
            }

            /// <summary>
            /// Reads a fixed number of bytes from the binary XML node stream.
            /// </summary>
            /// <param name="count">The number of bytes to read.</param>
            /// <returns>The bytes read from the node stream.</returns>
            private byte[] ReadNodeBytes(int count)
            {
                byte[] result = new byte[count];
                Array.Copy(data, nodeOffset, result, 0, count);
                nodeOffset += count;
                return result;
            }

            /// <summary>
            /// Reads a big-endian 32-bit integer from the binary XML node stream.
            /// </summary>
            /// <returns>The decoded integer.</returns>
            private int ReadNodeInt32()
            {
                return ReadInt32At(ref nodeOffset);
            }

            /// <summary>
            /// Reads a big-endian 32-bit integer from the binary XML data stream.
            /// </summary>
            /// <returns>The decoded integer.</returns>
            private int ReadDataInt32()
            {
                return ReadInt32At(ref dataOffset);
            }

            /// <summary>
            /// Reads a fixed number of bytes from the binary XML data stream.
            /// </summary>
            /// <param name="count">The number of bytes to read.</param>
            /// <returns>The bytes read from the data stream.</returns>
            private byte[] ReadDataBytes(int count)
            {
                return ReadBytesAt(ref dataOffset, count);
            }

            /// <summary>
            /// Reads bytes from an arbitrary cursor and advances that cursor.
            /// </summary>
            /// <param name="offset">The cursor to read from and update.</param>
            /// <param name="count">The number of bytes to read.</param>
            /// <returns>The bytes read at the supplied cursor.</returns>
            private byte[] ReadBytesAt(ref int offset, int count)
            {
                byte[] result = new byte[count];
                Array.Copy(data, offset, result, 0, count);
                offset += count;
                return result;
            }

            /// <summary>
            /// Reads a big-endian 32-bit integer from an arbitrary cursor and advances it.
            /// </summary>
            /// <param name="offset">The cursor to read from and update.</param>
            /// <returns>The decoded integer.</returns>
            private int ReadInt32At(ref int offset)
            {
                int result = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
                offset += 4;
                return result;
            }

            /// <summary>
            /// Advances the main data cursor to the next four-byte boundary.
            /// </summary>
            private void AlignData()
            {
                while ((dataOffset % 4) != 0)
                    dataOffset++;
            }

            /// <summary>
            /// Converts binary data to lower-case hexadecimal text.
            /// </summary>
            /// <param name="bytes">The bytes to format.</param>
            /// <returns>A lower-case hexadecimal string.</returns>
            private string ToHex(byte[] bytes)
            {
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private class KBinFormat
        {
            public int Size;
            public int Count;
            public bool Signed;
            public string Label;

            /// <summary>
            /// Resolves a binary XML node type to its scalar size, element count, and signedness.
            /// </summary>
            /// <param name="nodeType">The binary XML node type without array flags.</param>
            /// <returns>The format descriptor for the supplied node type.</returns>
            public static KBinFormat Get(int nodeType)
            {
                switch (nodeType)
                {
                    case 2: return new KBinFormat() { Size = 1, Count = 1, Signed = true, Label = "s8" };
                    case 3: return new KBinFormat() { Size = 1, Count = 1, Label = "u8" };
                    case 4: return new KBinFormat() { Size = 2, Count = 1, Signed = true, Label = "s16" };
                    case 5: return new KBinFormat() { Size = 2, Count = 1, Label = "u16" };
                    case 6: return new KBinFormat() { Size = 4, Count = 1, Signed = true, Label = "s32" };
                    case 7: return new KBinFormat() { Size = 4, Count = 1, Label = "u32" };
                    case 8: return new KBinFormat() { Size = 8, Count = 1, Signed = true, Label = "s64" };
                    case 9: return new KBinFormat() { Size = 8, Count = 1, Label = "u64" };
                    case 10: return new KBinFormat() { Size = 1, Count = -1, Label = "bin" };
                    case 11: return new KBinFormat() { Size = 1, Count = -1, Label = "str" };
                    case 13: return new KBinFormat() { Size = 4, Count = 1, Label = "float" };
                    case 16: return new KBinFormat() { Size = 1, Count = 2, Signed = true, Label = "s8" };
                    case 17: return new KBinFormat() { Size = 1, Count = 2, Label = "u8" };
                    case 18: return new KBinFormat() { Size = 2, Count = 2, Signed = true, Label = "s16" };
                    case 19: return new KBinFormat() { Size = 2, Count = 2, Label = "u16" };
                    case 20: return new KBinFormat() { Size = 4, Count = 2, Signed = true, Label = "s32" };
                    case 21: return new KBinFormat() { Size = 4, Count = 2, Label = "u32" };
                    case 26: return new KBinFormat() { Size = 1, Count = 3, Signed = true, Label = "s8" };
                    case 27: return new KBinFormat() { Size = 1, Count = 3, Label = "u8" };
                    case 28: return new KBinFormat() { Size = 2, Count = 3, Signed = true, Label = "s16" };
                    case 29: return new KBinFormat() { Size = 2, Count = 3, Label = "u16" };
                    case 30: return new KBinFormat() { Size = 4, Count = 3, Signed = true, Label = "s32" };
                    case 31: return new KBinFormat() { Size = 4, Count = 3, Label = "u32" };
                    case 36: return new KBinFormat() { Size = 1, Count = 4, Signed = true, Label = "s8" };
                    case 37: return new KBinFormat() { Size = 1, Count = 4, Label = "u8" };
                    case 38: return new KBinFormat() { Size = 2, Count = 4, Signed = true, Label = "s16" };
                    case 39: return new KBinFormat() { Size = 2, Count = 4, Label = "u16" };
                    case 40: return new KBinFormat() { Size = 4, Count = 4, Signed = true, Label = "s32" };
                    case 41: return new KBinFormat() { Size = 4, Count = 4, Label = "u32" };
                    case 48: return new KBinFormat() { Size = 1, Count = 16, Signed = true, Label = "s8" };
                    case 49: return new KBinFormat() { Size = 1, Count = 16, Label = "u8" };
                    case 50: return new KBinFormat() { Size = 2, Count = 8, Signed = true, Label = "s16" };
                    case 51: return new KBinFormat() { Size = 2, Count = 8, Label = "u16" };
                    case 52: return new KBinFormat() { Size = 1, Count = 1, Signed = true, Label = "s8" };
                    case 53: return new KBinFormat() { Size = 1, Count = 2, Signed = true, Label = "s8" };
                    case 54: return new KBinFormat() { Size = 1, Count = 3, Signed = true, Label = "s8" };
                    case 55: return new KBinFormat() { Size = 1, Count = 4, Signed = true, Label = "s8" };
                    case 56: return new KBinFormat() { Size = 1, Count = 16, Signed = true, Label = "s8" };
                    default:
                        throw new NotSupportedException("Unsupported IFS manifest node type " + nodeType.ToString() + ".");
                }
            }

            /// <summary>
            /// Converts a typed integer payload into the space-separated manifest text form.
            /// </summary>
            /// <param name="bytes">The raw big-endian payload bytes.</param>
            /// <param name="count">The number of scalar values in the payload.</param>
            /// <returns>The formatted manifest text.</returns>
            /// <summary>
            /// Gets the XML type name for this binary XML format.
            /// </summary>
            /// <param name="isArray">Whether the node uses array storage.</param>
            /// <returns>The XML type name, or null for implicit string data.</returns>
            public string GetTypeName(bool isArray)
            {
                if (Label == "str")
                    return null;
                if (Count > 1)
                    return Count.ToString() + Label;
                if (isArray)
                    return Label;
                return Label;
            }
            public string ToText(byte[] bytes, int count)
            {
                string[] values = new string[count];
                for (int i = 0; i < count; i++)
                    values[i] = ReadInteger(bytes, i * Size).ToString();
                return string.Join(" ", values);
            }

            /// <summary>
            /// Reads one signed or unsigned integer value from a typed payload.
            /// </summary>
            /// <param name="bytes">The raw big-endian payload bytes.</param>
            /// <param name="offset">The offset of the value within the payload.</param>
            /// <returns>The decoded integer value.</returns>
            private long ReadInteger(byte[] bytes, int offset)
            {
                long value = 0;
                for (int i = 0; i < Size; i++)
                    value = (value << 8) | bytes[offset + i];

                if (Signed)
                {
                    int bits = Size * 8;
                    long signBit = 1L << (bits - 1);
                    if ((value & signBit) != 0)
                        value -= 1L << bits;
                }
                return value;
            }
        }
    }
}
