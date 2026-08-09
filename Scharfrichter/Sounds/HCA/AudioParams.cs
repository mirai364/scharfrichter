namespace Scharfrichter.Codec.Sounds.HCA
{
    public struct AudioParams
    {
        public uint SimulatedLoopCount { get; set; }
        public bool InfiniteLoop { get; set; }
        public bool OutputWaveHeader { get; set; }

        public static AudioParams CreateDefault()
        {
            return new AudioParams
            {
                InfiniteLoop = false,
                SimulatedLoopCount = 0,
                OutputWaveHeader = true
            };
        }

        public static readonly AudioParams Default = CreateDefault();
    }
}