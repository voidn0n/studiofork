using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class BufferBindingJ
    {
        public BufferBindingJ() { }

        public BufferBindingJ(string name, int index)
        {
            Name = name;
            NameIndex = -1;
            Index = index;
            ArraySize = 0;
        }

        public string Name { get; set; } = string.Empty;
        public int NameIndex { get; set; }
        public int Index { get; set; }
        public int ArraySize { get; set; }
    }
}
