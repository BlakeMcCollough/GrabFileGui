using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace GrabDriver
{
    public abstract class Pipeline
    {
        public abstract void Open(string path);
        public abstract void Close();
        public abstract string Read();
    }


    public class FilePipe : Pipeline
    {
        private NamedPipeServerStream pipeServer;
        private UnicodeEncoding streamEncoding;
        private StreamReader infile;
        private string line;

        public override void Open(string path) //kind of like a constructor
        {
            infile = new StreamReader(path); //prepare the linereader
            pipeServer = new NamedPipeServerStream("myPipe", PipeDirection.InOut); //inout because I might need feedback from client
            pipeServer.WaitForConnection(); //stops from doing anything else until a client has connected
            streamEncoding = new UnicodeEncoding();
        }

        public override void Close()
        {
            infile.Close();
            pipeServer.Close();
        }

        public override string Read() //finds the file then reads it to line
        {
            line = infile.ReadLine();
            if (line == null)
            {
                infile.BaseStream.Position = 0;
                infile.DiscardBufferedData();
                line = infile.ReadLine();
            }
            return line;
        }

        public bool Write(string writing)
        {
            byte[] buff = streamEncoding.GetBytes(line);
            int len = buff.Length; //important to keep track of length

            try
            {
                pipeServer.WriteByte((byte)(len / 256));
                pipeServer.WriteByte((byte)(len & 255));
                pipeServer.Write(buff, 0, len);
                pipeServer.Flush();
            }
            catch(IOException e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
            return true;
        }
    }
}
