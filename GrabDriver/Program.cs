using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace GrabDriver
{
    class Program
    {
        static void Main(string[] args)
        {
            Regex stepAcceptRegex = new Regex(@"^\[\d\d Task info \d+]$");

            FilePipe pipeServerr = new FilePipe();
            pipeServerr.Open("..\\..\\..\\GRAB.181107.163454");

            string s = pipeServerr.Read();
            while (s != null)
            {
                if(pipeServerr.Write(s) == false)
                {
                    break;
                }
                if (stepAcceptRegex.IsMatch(s) == true)
                {
                    Console.WriteLine("Data sent, waiting a second");
                    Thread.Sleep(1000);
                }
                s = pipeServerr.Read();
            }

            pipeServerr.Close();
        }

    }
}
