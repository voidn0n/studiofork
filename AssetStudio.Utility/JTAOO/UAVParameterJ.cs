using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class UAVParameterJ
    {
        public UAVParameterJ() { }

        public UAVParameterJ(string name, int index, int originalIndex)
        {
            Name = name;
            NameIndex = -1;
            Index = index;
            OriginalIndex = originalIndex;
        }

        public string Name { get; set; } = string.Empty;
        public int NameIndex { get; set; }
        public int Index { get; set; }
        public int OriginalIndex { get; set; }
    }
}
