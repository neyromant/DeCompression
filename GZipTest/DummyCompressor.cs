using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// ДеКомпрессор, реализующий странную логику упаковки/распаковки:
    /// <remarks>
    /// При упаковке разбивает исходный файл на блоки по 1 мегабайту, пожимает их Gzip'ом и складывает в итоговый файл.
    /// При распаковке производит обратную операцию.
    /// </remarks>
    /// </summary>
    internal class DummyCompressor : IDisposable
    {
        private const int BlockSize = 1024 * 1024; //Размер блока - 1 МБ
        private readonly int _queueSize = Environment.ProcessorCount * 16;

        private readonly CompressionMode _mode;
        private readonly object _destLocker = new object();
        private readonly ManualResetEvent _cancellationToken;
        private readonly SimpleThreadPool _threadPool;
        private readonly Semaphore _addSemaphore;
        private readonly List<Exception> _exceptions;
        private int _lastProgress;

        public event EventHandler<ProgressEventArgs> OnProgress;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="mode">Режим работы</param>
        public DummyCompressor(CompressionMode mode)
        {
            _mode = mode;
            _addSemaphore = new Semaphore(_queueSize, _queueSize);
            _threadPool = new SimpleThreadPool();
            _cancellationToken = new ManualResetEvent(false);
            _exceptions = new List<Exception>();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _threadPool?.Dispose();
        }

        /// <summary>
        /// Выполняет упаковку / распаковку потока-источника в поток назначения
        /// </summary>
        /// <param name="source">Поток - источник</param>
        /// <param name="dest">Поток назначения</param>
        public void Process(Stream source, Stream dest)
        {
            _lastProgress = 0;
            switch (_mode)
            {
                case CompressionMode.Decompress:
                    Decompress(source, dest);
                    break;
                case CompressionMode.Compress:
                    Compress(source, dest);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_mode));
            }

            if (_cancellationToken.WaitOne(0))
                throw new AggregateException(_exceptions);
        }
        

        private void Compress(Stream source, Stream dest)
        {
            //Вычислим кол-во блоков
            var blocksCount = (int)(source.Length / BlockSize + (source.Length % BlockSize > 0 ? 1 : 0));

            //Создадим заголовок
            var header = new DummyCompressedDataHeader(blocksCount);
            //И оставим в потоке назначения место под запись заголовка
            dest.Seek(header.SelfSize, SeekOrigin.Begin);

            source.Seek(0, SeekOrigin.Begin);

            var blockIndex = 0;
            var destBlockIndex = 0;
            var doneEvent = new AutoResetEvent(false);
            while (source.Position < source.Length) //Пока не прочли весь исходный файл
            {
                if (_cancellationToken.WaitOne(0)) return; //Если было событие отмены - вывалимся
                var data = new byte[BlockSize]; //Читаем блок
                var dataLength = source.Read(data, 0, BlockSize);
                var index = blockIndex; 
                //Отправляем в очередь на упаковку
                AddToDeCompressionQueue(CompressionMode.Compress, data, dataLength, result => {
                        lock (_destLocker)
                        {
                            //После упаковки блока отметим в заголовке его расположение и реальный и исходный размеры
                            header.Blocks[destBlockIndex].Number = index;
                            header.Blocks[destBlockIndex].Size = result.Length; 
                            header.Blocks[destBlockIndex].SourceSize = dataLength;
                            //Ну и запишем его 
                            dest.Write(result, 0, result.Length);
                            //Если записали все, сигнализируем об этом
                            if (++destBlockIndex == blocksCount)
                                doneEvent.Set();
                        }
                });
                blockIndex++;
                ReportProgress(blockIndex, blocksCount);
            }

            doneEvent.WaitOne(); //Подождем пока все потоки доработают
            lock (_destLocker)
            {
                //Когда все закончено, встанем в начала файла назначения 
                dest.Seek(0, SeekOrigin.Begin);
                //И запишем заголовок
                header.WriteTo(dest);
            }
        }

        private void ReportProgress(int current, int all)
        {
            var currentProgress = (int) ((double) current / all * 100);
            if (_lastProgress != currentProgress)
            {
                _lastProgress = currentProgress;
                OnProgress?.Invoke(this, new ProgressEventArgs(_lastProgress));
            }
        }

        private void Decompress(Stream source, Stream dest)
        {
            var header = DummyCompressedDataHeader.ReadFrom(source); //Прочтем хидер

            dest.Seek(0, SeekOrigin.Begin);

            var blockIndex = 0;
            var writedBlockCount = 0;
            var doneEvent = new AutoResetEvent(false);
            while (source.Position < source.Length) //Начнем читать исходный файл
            {
                if (_cancellationToken.WaitOne(0)) return; //Если запросили прерывание - отвалимся

                var sourceBlockSize = header.Blocks[blockIndex].Size;
                var sourceBlockIndex = header.Blocks[blockIndex].Number;
                var data = new byte[sourceBlockSize];
                var dataLength = source.Read(data, 0, sourceBlockSize);
                if (dataLength != sourceBlockSize)
                    throw new FileLoadException("Исходный файл имеет неверный формат");

                AddToDeCompressionQueue(CompressionMode.Decompress, data, sourceBlockSize, result =>
                {
                    //Рассчитаем оригинальное смещение блока
                    var blockOffset = header.CalcSourceBlockOffset(sourceBlockIndex);

                    lock (_destLocker)
                    {
                        //И запишем его по оригинальному смещению
                        dest.Seek(blockOffset, SeekOrigin.Begin);
                        dest.Write(result, 0, result.Length);

                        if (++writedBlockCount == header.BlocksCount)
                            doneEvent.Set();
                    }
                });
                blockIndex++;
                ReportProgress(blockIndex, header.BlocksCount);
            }

            doneEvent.WaitOne();
        }
       

        private void AddToDeCompressionQueue(CompressionMode mode, byte[] data, int length, Action<byte[]> actionAfterCompress)
        {
            _addSemaphore.WaitOne();
            
            _threadPool.RunTask(() =>
            {
                try
                {
                    actionAfterCompress(
                        mode == CompressionMode.Compress
                            ? CompressBuffer(data, length)
                            : DecompressBuffer(data, length)
                    );

                    _addSemaphore.Release();
                }
                catch (Exception ex)
                {
                    _cancellationToken.Set();
                    lock (_exceptions)
                    {
                        _exceptions.Add(ex);
                    }
                }
            });
        }

        private static byte[] CompressBuffer(byte[] from, int length)
        {
            using (var result = new MemoryStream())
            {
                using (var compressionStream = new GZipStream(result, CompressionMode.Compress))
                {
                    compressionStream.Write(from, 0, length);
                }

                return result.ToArray();
            }
        }

        private static byte[] DecompressBuffer(byte[] from, int length)
        {
            using (var source = new MemoryStream(from, 0, length))
            {
                using (var dest = new MemoryStream())
                {
                    using (var compressionStream = new GZipStream(source, CompressionMode.Decompress))
                    {
                        compressionStream.CopyTo(dest);
                        return dest.ToArray();
                    }
                }
            }
        }
    }
}
