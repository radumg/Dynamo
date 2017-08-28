using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DSIronPython
{
    public class DebugWriter : FileStream
    {
        public DebugWriter(string path) : base(path, System.IO.FileMode.Append)
        {
        }

        public override void Write(byte[] array, int offset, int count)
        {
            base.Write(array, offset, count);
            base.Flush();
        }

    }
}
