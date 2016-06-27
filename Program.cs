using System;
using System.Collections.Generic;
using System.Text;

using PCSC;
using PCSC.Iso7816;

namespace MonitorReaderEvents
{
    class Program
    {
        static void Main(string[] args)
        {
            CardDumper dumper = new CardDumper();
            dumper.Start();

            // Let the program run until the user presses a key
            ConsoleKeyInfo keyinfo = Console.ReadKey();
            GC.KeepAlive(keyinfo);

            dumper.Stop();
        }

    }
}
