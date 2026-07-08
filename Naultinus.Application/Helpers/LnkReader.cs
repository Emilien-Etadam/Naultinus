using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Lit le chemin cible d'un fichier .lnk sans COM (compatible dotnet build).
    /// Basé sur le format binaire MS-SHLLINK.
    /// </summary>
    internal static class LnkReader
    {
        public static string? GetTargetPath(string lnkFilePath)
        {
            try
            {
                using var stream = File.OpenRead(lnkFilePath);
                using var br = new BinaryReader(stream);

                // En-tête : HeaderSize (4) + LinkCLSID (16) = 20 octets
                br.ReadBytes(0x14);

                uint linkFlags = br.ReadUInt32();

                // HasLinkTargetIDList (bit 0) : liste d'identifiants à ignorer
                if ((linkFlags & 0x01) != 0)
                {
                    ushort idListSize = br.ReadUInt16();
                    br.ReadBytes(idListSize);
                }

                // HasLinkInfo (bit 1) : structure avec le chemin local
                if ((linkFlags & 0x02) == 0)
                    return null;

                uint linkInfoSize = br.ReadUInt32();
                long linkInfoStart = stream.Position;

                uint linkInfoHeaderSize = br.ReadUInt32();
                uint linkInfoFlags = br.ReadUInt32();
                uint volumeIdOffset = br.ReadUInt32();
                uint localBasePathOffset = br.ReadUInt32();

                if (localBasePathOffset >= linkInfoSize)
                    return null;

                stream.Position = linkInfoStart + localBasePathOffset;
                string? path = ReadNullTerminatedString(br, Encoding.Unicode);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadNullTerminatedString(BinaryReader br, Encoding encoding)
        {
            var bytes = new List<byte>();
            for (; ; )
            {
                if (encoding == Encoding.Unicode)
                {
                    short b = br.ReadInt16();
                    if (b == 0) break;
                    bytes.Add((byte)(b & 0xFF));
                    bytes.Add((byte)(b >> 8));
                }
                else
                {
                    byte b = br.ReadByte();
                    if (b == 0) break;
                    bytes.Add(b);
                }
            }
            return bytes.Count == 0 ? null : encoding.GetString(bytes.ToArray());
        }
    }
}
