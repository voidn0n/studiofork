using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class TextureParameterJ
    {
        public TextureParameterJ() { }

        public TextureParameterJ(string name, int index, byte dimension, int sampler)
        {
            Name = name;
            NameIndex = -1;
            Index = index;
            Dim = dimension;
            SamplerIndex = sampler;
            MultiSampled = false;
        }

        public TextureParameterJ(string name, int index, byte dimension, int sampler, bool multiSampled) : this(name, index, dimension, sampler)
        {
            MultiSampled = multiSampled;
        }

        public string Name { get; set; } = string.Empty;
        public int NameIndex { get; set; }
        public int Index { get; set; }
        public int SamplerIndex { get; set; }
        public bool MultiSampled { get; set; }
        public byte Dim { get; set; }
    }
}
