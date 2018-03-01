using System;
using System.IO;

namespace GZipTest
{
    /// <summary>
    /// Расширения для Stream (Да, у нас на дворе новый 2008-й :)
    /// </summary>
    internal static class StreamExtensions
    {
        public static void CopyTo(this Stream src, Stream dest)
        {
            var size = src.CanSeek ? Math.Min((int)(src.Length - src.Position), 0x2000) : 0x2000;
            var buffer = new byte[size];
            int n;
            do
            {
                n = src.Read(buffer, 0, buffer.Length);
                dest.Write(buffer, 0, n);
            } while (n != 0);           
        }
    }
}
