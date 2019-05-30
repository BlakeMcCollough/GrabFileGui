using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrabFileGui
{
    class Task
    {
        public string TSK { get; set; }
        public string Client { get; set; }
        public string App { get; set; }
        public string Version { get; set; }
        public string IAR { get; set; }
        public string CK { get; set; }
        public string SVC { get; set; }

        public string CPU { get; set; }
        public TimeSpan CPUTime
        {
            get
            {
                return managedCPU;
            }
            set
            {
                if(totalCPU != TimeSpan.Zero)
                {
                    managedCPU = value - totalCPU;
                }
                totalCPU = value;
            }
        }
        public string File { get; set; }
        public int KeyCalls
        {
            get
            {
                return managedKeyCalls;
            }
            set
            {
                if(totalKeyCalls > 0)
                {
                    managedKeyCalls = value - totalKeyCalls;
                }
                totalKeyCalls = value;
            }
        }
        public int DACalls
        {
            get
            {
                return managedDACalls;
            }
            set
            {
                if (totalDACalls > 0)
                {
                    managedDACalls = value - totalDACalls;
                }
                totalDACalls = value;
            }
        }
        public int DskReads
        {
            get
            {
                return managedReads;
            }
            set
            {
                if (totalReads > 0)
                {
                    managedReads = value - totalReads;
                }
                totalReads = value;
            }
        }
        public int DskWrite
        {
            get
            {
                return managedWrites;
            }
            set
            {
                if (totalWrites > 0)
                {
                    managedWrites = value - totalWrites;
                }
                totalWrites = value;
            }
        }

        private TimeSpan managedCPU;
        private TimeSpan totalCPU = TimeSpan.Zero;
        private int managedWrites;
        private int managedReads;
        private int managedDACalls;
        private int managedKeyCalls;
        private int totalWrites = 0;
        private int totalReads = 0;
        private int totalDACalls = 0;
        private int totalKeyCalls = 0;
    }
}
