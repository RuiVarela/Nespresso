using System;
using System.Collections.Generic;
using System.Text;
using PCSC;
using PCSC.Iso7816;
using System.IO;
using System.Collections.Specialized;

namespace MonitorReaderEvents
{
    class CardDumper
    {
        SCardContext m_card_context = null;
        SCardMonitor m_monitor = null;
        Dictionary<string, byte[]> m_keys = new Dictionary<string, byte[]>();

        public CardDumper()
        {
            //LoadKeysFromFile(CardDumper.GetConfigurationKey("MfocLog"));
            LoadKeysFromDumpFile(CardDumper.GetConfigurationKey("MfocDump"));
        }

        void Log(string message)
        {
            Console.WriteLine(message);
            System.IO.File.AppendAllText("nespresso.log", message + "\n");
        }

        void LoadKeysFromDumpFile(string filename)
        {
            try
            {
                List<MifareClassicKey> keys = MifareClassic.GetKeysFromDump(System.IO.File.ReadAllBytes(filename));

                m_keys.Clear();
                for (int i = 0; i < 64; i++)
                {
                    m_keys["A" + i] = keys[i / 4].KeyA;
                    m_keys["B" + i] = keys[i / 4].KeyB;
                }
            }
            catch (Exception) 
            {
                Log("Unable to load keys from: " + filename);
            }
        }

        void LoadKeysFromFile(string filename)
        {
            m_keys.Clear();

            string[] lines = null;

            try
            {
                lines = File.ReadAllLines(filename);
            }
            catch (Exception) { }

            if (lines == null)
            {
                Log("Unable to load keys from: " + filename);
                return;
            }

            foreach (var line in lines)
            {
                if (!line.StartsWith("Block")) continue;

                string[] segments = line.Split(',');
                if ((segments == null) || (segments.Length != 3)) continue;

                segments[0] = segments[0].Replace("Block", "").Trim();

                int block = 0;
                if (!int.TryParse(segments[0], out block)) continue;

                string type = "";
                type = segments[1].Replace("type", "").Trim();

                segments = segments[2].Split(':');
                if ((segments == null) || (segments.Length != 2)) continue;

                string key = segments[0].Replace("key", "").Trim();

                byte[] binary = FromHex(key);

                //  Log("> " + block + " " + type + " " + key + " " + ToHex(binary));

                m_keys[type + block] = binary;
            }

        }

        public void Start()
        {
            m_card_context = new SCardContext();
            m_card_context.Establish(SCardScope.System);
            string[] readernames = m_card_context.GetReaders();

            // Create a monitor object with its own PC/SC context.
            m_monitor = new SCardMonitor(new SCardContext(), SCardScope.System);
            m_monitor.CardInserted += new CardInsertedEvent(CardInserted);
            m_monitor.CardRemoved += new CardRemovedEvent(CardRemoved);
            m_monitor.Initialized += new CardInitializedEvent(Initialized);
            m_monitor.StatusChanged += new StatusChangeEvent(StatusChanged);
            m_monitor.MonitorException += new MonitorExceptionEvent(MonitorException);

            foreach (string reader in readernames)
                Log("Start monitoring for reader " + reader + ".");

            m_monitor.Start(readernames);

            // Let the program run until the user presses a key
            ConsoleKeyInfo keyinfo = Console.ReadKey();
            GC.KeepAlive(keyinfo);
        }

        public void Stop()
        {
            if (m_monitor != null)
            {
                m_monitor.Cancel();
                m_monitor = null;
            }

            if (m_card_context != null)
            {
                m_card_context.Release();
                m_card_context = null;
            }
        }

        static Dictionary<string, byte> m_hexindex = null;
        static byte[] FromHex(string str)
        {
            if ((str.Length % 2) != 0) return null;

            if (m_hexindex == null)
            {
                m_hexindex = new Dictionary<string, byte>();
                for (byte i = 0; i < 255; i++)
                    m_hexindex.Add(i.ToString("X2"), i);
            }

            str = str.ToUpper();
            List<byte> hexres = new List<byte>();
            for (int i = 0; i < str.Length; i += 2)
                hexres.Add(m_hexindex[str.Substring(i, 2)]);

            return hexres.ToArray();
        }

        static string ToHex(byte[] atr)
        {
            if (atr == null)
                return null;

            StringBuilder sb = new StringBuilder();
            foreach (byte b in atr)
                sb.AppendFormat("{0:X2}", b);

            return sb.ToString();
        }

        void DumpStatus(SCardReader RFIDReader)
        {
            string[] names;  // contains the reader name(s)
            SCardProtocol proto;// contains the currently used communication protocol
            SCardState state;// contains the current state (flags)
            byte[] atr; // contains the card ATR

            SCardError rc = RFIDReader.Status(out names, out state, out proto, out atr);

            if (rc == SCardError.Success)
            {
                Log("Connected with protocol " + proto + " in state " + state);
                if (atr != null && atr.Length > 0)
                    Log("Card ATR: " + ToHex(atr));
            }
        }

        bool LoadKey(IsoCard card, byte[] key)
        {
            CommandApdu apdu = card.ConstructCommandApdu(IsoCase.Case3Short);
            apdu.CLA = 0xFF;
            apdu.Instruction = InstructionCode.ExternalAuthenticate;
            apdu.P1 = 0x20;
            apdu.P2 = 0x00;
            apdu.Data = key;

            Response response = card.Transmit(apdu);
            if (response.SW1 == (byte)SW1Code.Normal)
                return true;

            return false;
        }

        bool AuthBlock(IsoCard card, int block, bool isAKey = true)
        {
            byte type = 0x60;

            if (!isAKey)
                type = 0x61;

            CommandApdu apdu = card.ConstructCommandApdu(IsoCase.Case3Short);
            apdu.CLA = 0xFF;
            apdu.INS = 0x86;
            apdu.P1 = 0x00;
            apdu.P2 = 0x00;
            apdu.Data = new byte[] { 0x01, //version
                                     0x00, 
                                     (byte)block, 
                                     type, //Key type 0x60 TYPE_A, 0x61 TYPE_B
                                     0x00 };  //Key number 0x00 ~ 0x1F

            Response response = card.Transmit(apdu);
            if (response.SW1 == (byte)SW1Code.Normal)
                return true;

            return false;
        }

        string ReadBlock(IsoCard card, int block)
        {
            string ouput = "";

            CommandApdu apdu = card.ConstructCommandApdu(IsoCase.Case2Short);
            apdu.CLA = 0xFF;
            apdu.Instruction = InstructionCode.ReadBinary;
            apdu.P1 = 0x00;
            apdu.P2 = (byte)block;
            apdu.Le = 16;

            Response response = card.Transmit(apdu);
            if (response.SW1 == (byte)SW1Code.Normal)
            {
                byte[] data = response.GetData();
                if (data != null)
                    ouput = ToHex(data);
            }
            return ouput;
        }

        bool WriteBlock(IsoCard card, int block, byte[] data)
        {
            CommandApdu apdu = card.ConstructCommandApdu(IsoCase.Case3Short);
            apdu.CLA = 0xFF;
            apdu.Instruction = InstructionCode.UpdateBinary;
            apdu.P1 = 0x00;
            apdu.P2 = (byte)block;
            apdu.Data = data;

            Response response = card.Transmit(apdu);
            if (response.SW1 == (byte)SW1Code.Normal)
                return true;

            return false;
        }

        string Read(IsoCard card, int block)
        {
            string ouput = "";

            bool ok = false;

            byte[] key = null;
            if (m_keys.TryGetValue("A" + block, out key))
            {
                if (LoadKey(card, key))
                {
                    if (AuthBlock(card, block, true))
                    {
                        ouput = ReadBlock(card, block);
                        ok = !string.IsNullOrEmpty(ouput);
                    }
                }
            }

            if (!ok && m_keys.TryGetValue("B" + block, out key))
            {
                // Log("--" + ToHex(key));

                if (LoadKey(card, key))
                {
                    if (AuthBlock(card, block, false))
                    {
                        ouput = ReadBlock(card, block);
                        ok = !string.IsNullOrEmpty(ouput);
                    }
                }
            }


            return ouput;
        }

        bool Write(IsoCard card, int block, byte[] data)
        {
            bool ok = false;

            byte[] key = null;
            if (m_keys.TryGetValue("A" + block, out key))
                if (LoadKey(card, key) && AuthBlock(card, block, true))
                    ok = WriteBlock(card, block, data);

            if (!ok && m_keys.TryGetValue("B" + block, out key))
                if (LoadKey(card, key) && AuthBlock(card, block, false))
                    ok = WriteBlock(card, block, data);

            return ok;
        }


        string ReadUID(IsoCard card)
        {
            string ouput = "";

            CommandApdu apdu = card.ConstructCommandApdu(IsoCase.Case2Short);
            apdu.CLA = 0xFF;
            apdu.Instruction = InstructionCode.GetData;
            apdu.P1 = 0x00;
            apdu.P2 = 0x00;
            apdu.Le = 0x00;

            Response response = card.Transmit(apdu);
            if (response.SW1 == (byte)SW1Code.Normal)
            {
                byte[] data = response.GetData();
                if (data != null)
                    ouput = ToHex(data);
            }
            return ouput;
        }

        void DumpCard(IsoCard card)
        {
            string uid = ReadUID(card);
            Log("UID: " + uid);

            Log("[----------Start of Memory Dump----------]");

            int blockSize = 4;
            for (int sector = 0; sector != 16; ++sector)
            {
                Log("----------------Sector " + string.Format("{0:D2}", sector) + "-----------------");

                for (int block = 0; block != blockSize; ++block)
                {
                    int cardBlock = sector * blockSize + block;

                    string data = Read(card, cardBlock);

                    Log("Block " + string.Format("{0:D2}", cardBlock) + ": " + data);
                }
            }
            Log("[-----------End of Memory Dump-----------]");
        }

        string DumpMoney(IsoCard card)
        {
            string uid = ReadUID(card);
            string data = Read(card, 45);

            if (string.IsNullOrEmpty(data)) return "";

            string money = data.Substring(18, 4);

            byte[] binary = FromHex(money);
            int value = (((int)binary[0]) << 8) | ((int)binary[1]);

            Log("UID: " + uid + " " + value + " cents");

            return data;
        }

        void UpdateMoney(IsoCard card)
        {
            string cfg = CardDumper.GetConfigurationKey("UpdateMoney");
            int money = 0;

            if (!int.TryParse(cfg, out money)) return;

            string data = DumpMoney(card);
            if (string.IsNullOrEmpty(data)) return;

            int first = money & 0x0000FF;
            int second = (money >> 8) & 0x0000FF;

            //9
            // 10

            byte[] bytes = FromHex(data);
            bytes[9] = (byte)second;
            bytes[10] = (byte)first;

            if (!Write(card, 45, bytes))
            {
                Log("Write failed!");
            }

            DumpMoney(card);
        }

        void CardInserted(object sender, CardStatusEventArgs args)
        {
            SCardMonitor monitor = (SCardMonitor)sender;

            Log("CardInserted Event for reader: " + args.ReaderName);

            try
            {
                SCardReader reader = new SCardReader(m_card_context);

                if (reader.Connect(args.ReaderName, SCardShareMode.Shared, SCardProtocol.Any) == SCardError.Success)
                {
                    DumpStatus(reader);
                    IsoCard card = new IsoCard(reader);

                    if (CardDumper.IsKeyEnabled("DumpCard"))
                        DumpCard(card);

                    if (CardDumper.IsKeyEnabled("DumpMoney"))
                        DumpMoney(card);

                    if (!string.IsNullOrEmpty(CardDumper.GetConfigurationKey("UpdateMoney")))
                        UpdateMoney(card);
                }

                reader.Disconnect(SCardReaderDisposition.Reset);
            }
            catch (Exception ex)
            {
                Log("Exception: " + ex.ToString());
            }
        }

        void CardRemoved(object sender, CardStatusEventArgs args)
        {
            SCardMonitor monitor = (SCardMonitor)sender;
            Log("CardRemoved Event for reader: " + args.ReaderName);
            //Console.WriteLine("   ATR: " + StringAtr(args.Atr));
            //Console.WriteLine("   State: " + args.State + "\n");
        }

        void Initialized(object sender, CardStatusEventArgs args)
        {
            SCardMonitor monitor = (SCardMonitor)sender;
            //Console.WriteLine(">> Initialized Event for reader: " + args.ReaderName);
            //Console.WriteLine("   ATR: " + StringAtr(args.Atr));
            //Console.WriteLine("   State: " + args.State + "\n");
        }

        void StatusChanged(object sender, StatusChangeEventArgs args)
        {
            SCardMonitor monitor = (SCardMonitor)sender;
            //Console.WriteLine(">> StatusChanged Event for reader: " + args.ReaderName);
            //Console.WriteLine("   ATR: " + StringAtr(args.ATR));
            //Console.WriteLine("   Last state: " + args.LastState + "\n   New state: " + args.NewState + "\n");
        }

        void MonitorException(object sender, PCSCException ex)
        {
            Log("Monitor exited due an error:");
            Log(SCardHelper.StringifyError(ex.SCardError));
        }



        public static bool IsKeyEnabled(string key)
        {
            return (GetConfigurationKey(key) == "1") || (GetConfigurationKey(key).ToUpper() == "TRUE");
        }

        public static string GetConfigurationKey(string key)
        {
            string output = "";

            NameValueCollection settings = null;
            try
            {
                settings = System.Configuration.ConfigurationManager.AppSettings;
                if (settings != null)
                {
                    output = settings[key];
                }
            }
            catch (System.Configuration.ConfigurationErrorsException ex)
            { }

            return output;
        }


    }
}
