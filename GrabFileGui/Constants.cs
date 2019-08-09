using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GrabFileGui
{
    class Constants
    {
        public readonly double GRAPH_MARGIN;
        public readonly int LENGTH_DSK;
        public readonly int MAX_DSK_LOGS;
        public readonly int SEC;
        public readonly int SORT_TSK, SORT_KEY, SORT_DA, SORT_RD, SORT_WR;
        public readonly int TSK, CLNT, APP, VER, IAR, CK, SVC, CPU, FILE, KEY, DA, RD, WR; //corresponds to index in task list
        public readonly int[] LENGTH_TSK; //list of sizes corresponding to task consts
        public readonly int DTSK, SEIZE, QUEUE, DAPP, DIAR, TIME; //corresponds to index in disklog list
        public readonly int TIME_DOT;

        public readonly Regex TASK_REGEX;
        public readonly Regex STEP_REGEX;
        public readonly Regex DISK_REGEX;
        public readonly Regex COPYRIGHT_REGEX;
        public readonly Regex STARTUP_REGEX;

        public Constants()
        {
            GRAPH_MARGIN = 10;
            LENGTH_DSK = 10;
            MAX_DSK_LOGS = 20;
            SEC = 1;
            SORT_TSK = 6; SORT_KEY = 2; SORT_DA = 3; SORT_RD = 4; SORT_WR = 5;
            TSK = 0; CLNT = 1; APP = 2; VER = 3; IAR = 4; CK = 5; SVC = 6; CPU = 7; FILE = 8; KEY = 9; DA = 10; RD = 11; WR = 12;
            LENGTH_TSK = new int[] { 3, 12, 8, 8, 4, 2, 2, 12, 7, 8, 8, 8, 8 };
            DTSK = 2; SEIZE = 4; QUEUE = 6; DAPP = 7; DIAR = 8; TIME = 9;
            TIME_DOT = 8;

            //checks for three numbers, 12 of any characters, any word (including .) and the rest of the data entry in the format of the grab file
            //the purpose is to check if the line read is a data entry
            TASK_REGEX = new Regex(@"^\d{3} .{12} .{8} .{8} \w{4} \w\w \w\w \d\d:\d\d:\d\d:\d\d\d .{7} \w{8} \w{8} \w{8} \w{8} *$");
            //similar to tskAccept, but it checks if it's a header to a new second
            STEP_REGEX = new Regex(@"^\[\d\d Task info \d+]$");
            //similar to tskAccept, but checks for disk seize header
            DISK_REGEX = new Regex(@"^\[\d\d Disk seize]$");
            //this is just used to find the copyright section, the while loop should read through it until startup section is found
            COPYRIGHT_REGEX = new Regex(@"^\[\d\d Copyright]$");
            //is to be used after copyright box is read, indicating startup messages
            STARTUP_REGEX = new Regex(@"^\[\d\d Startup messages]$");
        }
    }
}
