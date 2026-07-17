using Scharfrichter.Codec.Charts;

using System.Collections.Generic;

namespace Scharfrichter.Codec.Archives
{
    public sealed class BmsonSoundLayout
    {
        public List<BmsonSoundTrack> Tracks = new List<BmsonSoundTrack>();
        public Dictionary<Entry, BmsonPackedNote> Notes = new Dictionary<Entry, BmsonPackedNote>();
    }

    public sealed class BmsonSoundTrack
    {
        public string Name;
        public int Index;
    }

    public sealed class BmsonPackedNote
    {
        public int TrackIndex;
        public bool Continue;
    }
}