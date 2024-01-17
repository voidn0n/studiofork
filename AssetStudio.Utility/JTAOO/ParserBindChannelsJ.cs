using AssetStudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class ParserBindChannelsJ
    {
        public ParserBindChannelsJ() { }

        public ParserBindChannelsJ(ShaderBindChannelJ[] channels, int sourceMap)
        {
            Channels = channels;
            SourceMap = sourceMap;
        }

        public ShaderBindChannelJ[] Channels { get; set; } = System.Array.Empty<ShaderBindChannelJ>();
        public int SourceMap { get; set; }
    }
}
