using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Scharfrichter.Codec.ACB
{
    public sealed class AcbCueRecord
    {
        internal AcbCueRecord()
        {
        }

        public uint CueId { get; set; }
        public byte ReferenceType { get; set; }
        public ushort ReferenceIndex { get; set; }

        public bool IsWaveformIdentified { get; set; }
        public ushort WaveformIndex { get; set; }
        public ushort WaveformId { get; set; }
        public byte EncodeType { get; set; }
        public bool IsStreaming { get; set; }

        public string CueName { get; set; }
    }

    internal enum WaveformEncodeType : byte
    {
        Adx = 0,
        Hca = 2,
        HcaAlt = 6,
        Vag = 7,
        Atrac3 = 8,
        BcWav = 9,
        NintendoDsp = 13
    }

    internal static class AwbFileNameFormats
    {
        public static readonly string Format1 = "{0}_streamfiles.awb";
        public static readonly string Format2 = "{0}.awb";
        public static readonly string Format3 = "{0}_STR.awb";
    }

    public sealed class AcbFile : UtfTable
    {
        public static AcbFile FromStream(FileStream stream)
        {
            return FromStream(stream, 0, stream.Length, false);
        }

        public static AcbFile FromStream(FileStream stream, bool disposeStream)
        {
            return FromStream(stream, 0, stream.Length, disposeStream);
        }

        public static AcbFile FromStream(FileStream stream, long offset, long size)
        {
            return new AcbFile(stream, offset, size, stream.Name, false);
        }

        public static AcbFile FromStream(FileStream stream, long offset, long size, bool disposeStream)
        {
            return new AcbFile(stream, offset, size, stream.Name, disposeStream);
        }

        public static AcbFile FromStream(Stream stream, string acbFileName, bool disposeStream)
        {
            return FromStream(stream, 0, stream.Length, acbFileName, disposeStream);
        }

        public static AcbFile FromStream(Stream stream, long offset, long size, string acbFileName, bool disposeStream)
        {
            return new AcbFile(stream, offset, size, acbFileName, disposeStream);
        }

        public static AcbFile FromFile(string fileName)
        {
            FileStream fs = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            return FromStream(fs, false);
        }

        public Dictionary<string, UtfTable> Tables => _tables;

        public AcbCueRecord[] Cues => _cues;

        public Afs2Archive InternalAwb => _internalAwb;

        public Afs2Archive ExternalAwb => _externalAwb;

        public uint FormatVersion
        {
            get
            {
                if (_formatVersion == null)
                {
                    _formatVersion = GetFieldValueAsNumber<uint>(0, "Version");
                }

                return _formatVersion ?? 0;
            }
        }

        public UtfTable GetTable(string tableName)
        {
            if (_tables == null)
            {
                return null;
            }
            Dictionary<string, UtfTable> tables = _tables;
            UtfTable table;
            if (tables.ContainsKey(tableName))
            {
                table = tables[tableName];
            }
            else
            {
                table = ResolveTable(tableName);
                if (table != null)
                {
                    tables.Add(tableName, table);
                }
            }
            return table;
        }

        public string[] GetFileNames()
        {
            return _fileNames ?? (_fileNames = _cues?.Select(cue => cue.CueName).ToArray());
        }

        public bool FileExists(string fileName)
        {
            return _fileNames != null && _fileNames.Contains(fileName);
        }

        public Stream OpenDataStream(string fileName)
        {
            AcbCueRecord cue;
            try
            {
                cue = Cues.Single(c => c.CueName == fileName);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"File '{fileName}' is not found or it has multiple entries.", ex);
            }
            return GetDataStreamFromCueInfo(cue, fileName);
        }

        public Stream OpenDataStream(uint cueId)
        {
            AcbCueRecord cue;
            string tempFileName = $"cue #{cueId}";
            try
            {
                cue = Cues.Single(c => c.CueId == cueId);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"File '{tempFileName}' is not found or it has multiple entries.", ex);
            }
            return GetDataStreamFromCueInfo(cue, tempFileName);
        }

        public static string GetSymbolicFileNameFromCueId(uint cueId)
        {
            return $"dat_{cueId:000000}.bin";
        }

        internal override void Initialize()
        {
            base.Initialize();
            InitializeAcbTables();
            InitializeCueNameToWaveformTable();
            InitializeAwbArchives();
        }

        protected override void Dispose(bool disposing)
        {
            _internalAwb?.Dispose();
            _externalAwb?.Dispose();
            base.Dispose(disposing);
        }

        private AcbFile(Stream stream, long offset, long size, string acbFileName, bool disposeStream)
            : base(stream, offset, size, acbFileName, disposeStream)
        {
            Initialize();
        }

        private void InitializeAcbTables()
        {
            Stream stream = Stream;
            long refItemOffset = 0, refItemSize = 0, refCorrection = 0;

            Dictionary<string, UtfTable> tables = new Dictionary<string, UtfTable>();
            _tables = tables;

            UtfTable cueTable = GetTable("CueTable");
            UtfTable waveformTable = GetTable("WaveformTable");
            UtfTable synthTable = GetTable("SynthTable");
            AcbCueRecord[] cues = new AcbCueRecord[cueTable.Rows.Length];
            _cues = cues;

            for (int i = 0; i < cues.Length; ++i)
            {
                AcbCueRecord cue = new AcbCueRecord();
                cue.IsWaveformIdentified = false;
                cue.CueId = cueTable.GetFieldValueAsNumber<uint>(i, "CueId").Value;
                cue.ReferenceType = cueTable.GetFieldValueAsNumber<byte>(i, "ReferenceType").Value;
                cue.ReferenceIndex = cueTable.GetFieldValueAsNumber<ushort>(i, "ReferenceIndex").Value;
                cues[i] = cue;

                switch (cue.ReferenceType)
                {
                    case 2:
                        {
                            refItemOffset = synthTable.GetFieldOffset(cue.ReferenceIndex, "ReferenceItems").Value;
                            refItemSize = synthTable.GetFieldSize(cue.ReferenceIndex, "ReferenceItems").Value;
                            refCorrection = refItemSize + 2;
                            break;
                        }
                    case 3:
                    case 8:
                        {
                            if (i == 0)
                            {
                                long? refItemOffsetNullable = synthTable.GetFieldOffset(0, "ReferenceItems");
                                if (refItemOffsetNullable == null)
                                {
                                    throw new FormatException("ReferenceItems field is missing.");
                                }

                                refItemOffset = refItemOffsetNullable.Value;
                                long? refItemSizeNullable = synthTable.GetFieldSize(0, "ReferenceItems");
                                if (refItemSizeNullable == null)
                                {
                                    throw new FormatException("ReferenceItems field is missing.");
                                }

                                refItemSize = refItemSizeNullable.Value;
                                refCorrection = refItemSize - 2;
                            }
                            else
                            {
                                refCorrection += 4;
                            }

                            break;
                        }
                    default:
                        throw new FormatException($"Unexpected ReferenceType '{cues[i].ReferenceType}' for CueIndex: '{i}.'");
                }

                if (refItemSize != 0)
                {
                    cue.WaveformIndex = PeekUInt16BEAt(stream, refItemOffset + refCorrection);
                    byte? isStreamingNullable = waveformTable.GetFieldValueAsNumber<byte>(cue.WaveformIndex, "Streaming");
                    if (isStreamingNullable != null)
                    {
                        cue.IsStreaming = isStreamingNullable.Value != 0;

                        ushort? waveformIdNullable = waveformTable.GetFieldValueAsNumber<ushort>(cue.WaveformIndex, "Id");
                        if (waveformIdNullable != null)
                        {
                            cue.WaveformId = waveformIdNullable.Value;
                        }
                        else if (cue.IsStreaming)
                        {
                            waveformIdNullable = waveformTable.GetFieldValueAsNumber<ushort>(cue.WaveformIndex, "StreamAwbId");
                            if (waveformIdNullable == null)
                            {
                                throw new FormatException("StreamAwbId field is missing.");
                            }

                            cue.WaveformId = waveformIdNullable.Value;
                        }
                        else
                        {
                            waveformIdNullable = waveformTable.GetFieldValueAsNumber<ushort>(cue.WaveformIndex, "MemoryAwbId");
                            if (waveformIdNullable == null)
                            {
                                throw new FormatException("MemoryAwbId field is missing.");
                            }

                            cue.WaveformId = waveformIdNullable.Value;
                        }

                        byte? encTypeNullable = waveformTable.GetFieldValueAsNumber<byte>(cue.WaveformIndex, "EncodeType");
                        if (encTypeNullable == null)
                        {
                            throw new FormatException("EncodeType field is missing.");
                        }

                        cue.EncodeType = encTypeNullable.Value;

                        cue.IsWaveformIdentified = true;
                    }
                }
            }
        }

        private void InitializeCueNameToWaveformTable()
        {
            UtfTable cueNameTable = GetTable("CueNameTable");
            AcbCueRecord[] cues = Cues;
            Dictionary<string, ushort> cueNameToWaveform = new Dictionary<string, ushort>();
            _cueNameToWaveform = cueNameToWaveform;

            for (int i = 0; i < cueNameTable.Rows.Length; ++i)
            {
                ushort? cueIndexNullable = cueNameTable.GetFieldValueAsNumber<ushort>(i, "CueIndex");
                if (cueIndexNullable == null)
                {
                    throw new FormatException("CueIndex field is missing.");
                }

                ushort cueIndex = cueIndexNullable.Value;
                AcbCueRecord cue = cues[cueIndex];
                if (cue.IsWaveformIdentified)
                {
                    string cueName = cueNameTable.GetFieldValueAsString(i, "CueName");
                    if (cueName == null)
                    {
                        throw new FormatException("CueName field is missing.");
                    }

                    cueName += GetExtensionForEncodeType(cue.EncodeType);

                    cue.CueName = cueName;
                    cueNameToWaveform.Add(cueName, cue.WaveformId);
                }
            }
        }

        private void InitializeAwbArchives()
        {
            long? externalAwbSize = GetFieldSize(0, "StreamAwbAfs2Header");
            if (externalAwbSize.HasValue && externalAwbSize.Value > 0)
            {
                _externalAwb = GetExternalAwbArchive();
            }

            long? internalAwbSize = GetFieldSize(0, "AwbFile");
            if (internalAwbSize.HasValue && internalAwbSize.Value > 0)
            {
                _internalAwb = GetInternalAwbArchive();
            }
        }

        private Stream GetDataStreamFromCueInfo(AcbCueRecord cue, string fileNameForErrorInfo)
        {
            if (!cue.IsWaveformIdentified)
            {
                throw new InvalidOperationException($"File '{fileNameForErrorInfo}' is not identified.");
            }

            Stream result;

            if (cue.IsStreaming)
            {
                Afs2Archive externalAwb = ExternalAwb;
                if (externalAwb == null)
                {
                    throw new InvalidOperationException($"External AWB does not exist for streaming file '{fileNameForErrorInfo}'.");
                }

                if (!externalAwb.Files.ContainsKey(cue.WaveformId))
                {
                    throw new InvalidOperationException($"Waveform ID {cue.WaveformId} is not found in AWB file {externalAwb.FileName}.");
                }

                Afs2FileRecord targetExternalFile = externalAwb.Files[cue.WaveformId];

                using (FileStream fs = File.Open(externalAwb.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    result = ExtractToNewStream(fs, targetExternalFile.FileOffsetAligned, (int)targetExternalFile.FileLength);
                }
            }
            else
            {
                Afs2Archive internalAwb = InternalAwb;

                if (internalAwb == null)
                {
                    throw new InvalidOperationException($"Internal AWB is not found for memory file '{fileNameForErrorInfo}' in '{AcbFileName}'.");
                }

                if (!internalAwb.Files.ContainsKey(cue.WaveformId))
                {
                    throw new InvalidOperationException($"Waveform ID {cue.WaveformId} is not found in internal AWB in {AcbFileName}.");
                }

                Afs2FileRecord targetInternalFile = internalAwb.Files[cue.WaveformId];

                result = ExtractToNewStream(Stream, targetInternalFile.FileOffsetAligned, (int)targetInternalFile.FileLength);
            }

            return result;
        }

        private Afs2Archive GetInternalAwbArchive()
        {
            long? internalAwbOffset = GetFieldOffset(0, "AwbFile");
            if (internalAwbOffset == null)
            {
                throw new FormatException("AwbFile field is missing.");
            }

            Afs2Archive internalAwb = new Afs2Archive(Stream, internalAwbOffset.Value, AcbFileName, false);
            internalAwb.Initialize();
            return internalAwb;
        }

        private Afs2Archive GetExternalAwbArchive()
        {
            string acbFileName = AcbFileName;
            string awbDirPath = Path.GetDirectoryName(acbFileName);

            if (awbDirPath == null)
            {
                awbDirPath = string.Empty;
            }

            string awbBaseFileName = Path.GetFileNameWithoutExtension(acbFileName);
            string[] awbFiles = null;

            if (awbFiles == null || awbFiles.Length < 1)
            {
                string awbMask1 = string.Format(AwbFileNameFormats.Format1, awbBaseFileName);
                awbFiles = Directory.GetFiles(awbDirPath, awbMask1, SearchOption.TopDirectoryOnly);
            }

            if (awbFiles == null || awbFiles.Length < 1)
            {
                string awbMask2 = string.Format(AwbFileNameFormats.Format2, awbBaseFileName);
                awbFiles = Directory.GetFiles(awbDirPath, awbMask2, SearchOption.TopDirectoryOnly);
            }

            if (awbFiles == null || awbFiles.Length < 1)
            {
                string awbMask3 = string.Format(AwbFileNameFormats.Format3, awbBaseFileName);
                awbFiles = Directory.GetFiles(awbDirPath, awbMask3, SearchOption.TopDirectoryOnly);
            }

            if (awbFiles.Length < 1)
            {
                throw new FileNotFoundException($"Cannot find AWB file. Please verify corresponding AWB file is named '{string.Format(AwbFileNameFormats.Format1, awbBaseFileName)}', '{string.Format(AwbFileNameFormats.Format2, awbBaseFileName)}', or '{string.Format(AwbFileNameFormats.Format3, awbBaseFileName)}'.");
            }

            if (awbFiles.Length > 1)
            {
                throw new FileNotFoundException($"More than one matching AWB file for this ACB. Please verify only one AWB file is named '{string.Format(AwbFileNameFormats.Format1, awbBaseFileName)}', '{string.Format(AwbFileNameFormats.Format2, awbBaseFileName)}' or '{string.Format(AwbFileNameFormats.Format3, awbBaseFileName)}'.");
            }

            byte[] externalAwbHash = GetFieldValueAsData(0, "StreamAwbHash");
            FileStream fs = File.Open(awbFiles[0], FileMode.Open, FileAccess.Read, FileShare.Read);

            Afs2Archive archive = new Afs2Archive(fs, 0, fs.Name, true);
            archive.Initialize();

            return archive;
        }

        private UtfTable ResolveTable(string tableName)
        {
            long? tableOffset = GetFieldOffset(0, tableName);
            if (!tableOffset.HasValue)
            {
                return null;
            }

            long? tableSize = GetFieldSize(0, tableName);
            if (!tableSize.HasValue)
            {
                return null;
            }

            UtfTable table = new UtfTable(Stream, tableOffset.Value, tableSize.Value, AcbFileName, false);
            table.Initialize();
            return table;
        }

        private static string GetExtensionForEncodeType(byte encodeType)
        {
            string ext;
            WaveformEncodeType et = (WaveformEncodeType)encodeType;
            switch (et)
            {
                case WaveformEncodeType.Adx:
                    ext = ".adx";
                    break;
                case WaveformEncodeType.Hca:
                case WaveformEncodeType.HcaAlt:
                    ext = ".hca";
                    break;
                case WaveformEncodeType.Atrac3:
                    ext = ".at3";
                    break;
                case WaveformEncodeType.Vag:
                    ext = ".vag";
                    break;
                case WaveformEncodeType.BcWav:
                    ext = ".bcwav";
                    break;
                case WaveformEncodeType.NintendoDsp:
                    ext = ".dsp";
                    break;
                default:
                    ext = $".et-{encodeType:D2}.bin";
                    break;
            }

            return ext;
        }

        private static ushort PeekUInt16BEAt(Stream stream, long offset)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] data = new byte[2];
            int totalRead = 0;
            while (totalRead < 2)
            {
                int read = stream.Read(data, totalRead, 2 - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            stream.Position = originalPosition;
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        private Dictionary<string, UtfTable> _tables;
        private Dictionary<string, ushort> _cueNameToWaveform;
        private AcbCueRecord[] _cues;
        private Afs2Archive _internalAwb;
        private Afs2Archive _externalAwb;
        private string[] _fileNames;
        private uint? _formatVersion;
    }
}