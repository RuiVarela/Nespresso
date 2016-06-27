using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonitorReaderEvents
{
    public class MifareClassic
    {
        public MifareClassic()
        {

        }

        public static List<MifareClassicKey> GetKeysFromDump(byte[] dump)
        {
            List<MifareClassicKey> result = null;

            if (dump != null)
            {
                result = new List<MifareClassicKey>();

                for (int i = 0; i < 16; i++)
                {
                    MifareClassicKey key = new MifareClassicKey();
                    key.Sector = i;

                    //Key A
                    Buffer.BlockCopy(dump, (i * 64) + 48, key.KeyA, 0, 6);

                    //Access conditions
                    Buffer.BlockCopy(dump, (i * 64) + 54, key.AccessConditions, 0, 4);

                    //Key B
                    Buffer.BlockCopy(dump, (i * 64) + 58, key.KeyB, 0, 6);

                    result.Add(key);
                }
            }

            return result;
        }
    }

    public class MifareClassicKey
    {
        public int Sector;
        public byte[] KeyA;
        public byte[] KeyB;
        public byte[] AccessConditions;

        public MifareClassicKey()
        {
            Sector = 0;
            KeyA = new byte[6];
            KeyB = new byte[6];
            AccessConditions = new byte[4];
        }

        public MifareClassicKey(int sector, byte[] keyA, byte[] keyB)
        {
            Sector = sector;
            KeyA = keyA;
            KeyB = keyB;
        }
    }
}
