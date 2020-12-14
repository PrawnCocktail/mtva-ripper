using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mtva
{
    public class targetJson
    {
        public string Source { get; set; }
        public string Url { get; set; }
        public string Token { get; set; }
    }

    public class Streams
    {
        public int Bandwidth { get; set; }
        public string Resolution { get; set; }
        public string Playlist { get; set; }
    }

    public class Chunks
    {
        public byte[] Key { get; set; }
        public List<string> Streams { get; set; }
    }
}
