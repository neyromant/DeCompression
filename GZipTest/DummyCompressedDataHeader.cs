using System;
using System.IO;
using System.Linq;

namespace GZipTest
{
    /// <summary>
    /// Описывает заголовок упакованных данных, 
    /// а так же предоставляет функционал для его записи/чтения в поток / из потока
    /// </summary>
    internal class DummyCompressedDataHeader
    {
        private const uint Header = 0xDEADBEEF; //Мертвая говядина - наше все

        /// <summary>
        /// Общее количество блоков
        /// </summary>
        public int BlocksCount { get; }

        /// <summary>
        /// Описание блоков
        /// </summary>
        public Block[] Blocks { get; }

        /// <summary>
        /// Размер заголовка как такового
        /// </summary>
        public int SelfSize => sizeof(uint) + sizeof(int) + Block.SelfSize * BlocksCount;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="blocksCount">Количество блоков</param>
        public DummyCompressedDataHeader(int blocksCount)
        {
            if (blocksCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(blocksCount), "Значение должно быть больше нуля");

            BlocksCount = blocksCount;
            Blocks = Enumerable.Range(0, blocksCount).Select(x=>new Block()).ToArray();
        }

        /// <summary>
        /// Записывает заголовок в указанный поток
        /// </summary>
        /// <param name="destination">Поток назначения</param>
        internal void WriteTo(Stream destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (!destination.CanWrite)
                throw new ArgumentException("Запись в указанный поток невозможна", nameof(destination));

            var bwriter = new BinaryWriter(destination);
            
                bwriter.Write(Header);
                bwriter.Write(BlocksCount);

                foreach (var blockSize in Blocks)
                    blockSize.WriteTo(bwriter);
        }

        /// <summary>
        /// Читает заголовок из указанного потока
        /// </summary>
        /// <param name="source">Поток-источник</param>
        /// <returns></returns>
        internal static DummyCompressedDataHeader ReadFrom(Stream source)
        {
            var breader = new BinaryReader(source);
            var header = breader.ReadUInt32();
            if (header != Header)
                throw new FileLoadException("Указанный файл имеет неверный формат (Заголовок не соответствует ожидаемому)");

            var count = breader.ReadInt32();
            var result = new DummyCompressedDataHeader(count);
            for (var idx = 0; idx < count; idx++)
                result.Blocks[idx].ReadFrom(breader);
            return result;
        }

        internal long CalcSourceBlockOffset(int idx)
        {
            if (idx < 0 || idx >= BlocksCount)
                throw new ArgumentOutOfRangeException(nameof(idx));

            var offset = Blocks.Where(b=>b.Number<idx).Sum(x=>(long)x.SourceSize);
            return offset;
        }


        /// <summary>
        /// Описывает блок в заголовке
        /// </summary>
        public class Block
        {
            /// <summary>
            /// Порядковый номер
            /// </summary>
            public int Number { get; set; } = -1;

            /// <summary>
            /// Размер блока в запакованом виде
            /// </summary>
            public int Size { get; set; } = -1;

            /// <summary>
            /// Исходный размер блока
            /// </summary>
            public int SourceSize { get; set; } = -1;

            /// <summary>
            /// Собственный размер структуры
            /// </summary>
            public static int SelfSize => sizeof(int) + sizeof(int) + sizeof(int);

            public void WriteTo(BinaryWriter bwriter)
            {
                bwriter.Write(Number);
                bwriter.Write(Size);
                bwriter.Write(SourceSize);
            }

            public void ReadFrom(BinaryReader breader)
            {
                Number = breader.ReadInt32();
                Size = breader.ReadInt32();
                SourceSize = breader.ReadInt32();
            }
        }
    }
}
