using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace GrabFileGui
{
    public abstract class Pipeline
    {
        public abstract bool Open(string path);
        public abstract void Close();
        public abstract string Read();
    }


    public class ClientPipe : Pipeline
    {
        private NamedPipeClientStream pipeClient;
        private UnicodeEncoding streamEncoding;
        public override bool Open(string serverName)
        {
            pipeClient = new NamedPipeClientStream(serverName, "myPipe", PipeDirection.In, PipeOptions.None);
            try
            {
                pipeClient.Connect(5000);
            }
            catch(TimeoutException e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
            streamEncoding = new UnicodeEncoding();
            return true;
        }

        public override void Close()
        {
            pipeClient.Close();
        }

        public override string Read()
        {
            int len = pipeClient.ReadByte() * 256;
            len = len + pipeClient.ReadByte();
            if(len < 0)
            {
                return null;
            }

            byte[] buff;
            try
            {
                buff = new byte[len];
            }
            catch(OverflowException)
            {
                return null;
            }
            pipeClient.Read(buff, 0, len);
            return streamEncoding.GetString(buff);
        }
    }
}
