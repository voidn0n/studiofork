using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class SamplerParameterJ
    {
        public SamplerParameterJ() { }

        public SamplerParameterJ(uint sampler, int bindPoint)
        {
            Sampler = sampler;
            BindPoint = bindPoint;
        }

        public uint Sampler { get; set; }
        public int BindPoint { get; set; }
    }
}
