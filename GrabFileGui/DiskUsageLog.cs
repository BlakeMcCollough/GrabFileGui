using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrabFileGui
{
    class DiskUsageLog //this effectively works as a C struct
    {
        public int Sec { get; set; }
        public string TSK { get; set; }
        public string Seize { get; set; }
        public string Queue { get; set; }
        public string App { get; set; }
        public string IAR { get; set; }
        public string TimeStamp { get; set; }
    }
}
