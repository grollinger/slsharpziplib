using System;
using System.IO;
using System.Reflection;
//using System.Runtime.Serialization.Formatters.Binary;
using System.Security;
using System.Text;
using System.Threading;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Checksums;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Tests.TestSupport;




using Microsoft.Silverlight.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ICSharpCode.SharpZipLib.Tests.Zip
{
    #region Local Support Classes
    class RuntimeInfo
    {
        public RuntimeInfo(CompressionMethod method, int compressionLevel, int size, string password, bool getCrc)
        {
            this.method = method;
            this.compressionLevel = compressionLevel;
            this.password = password;
            this.size = size;
            this.random = false;

            original = new byte[Size];
            if (random)
            {
                System.Random rnd = new Random();
                rnd.NextBytes(original);
            }
            else
            {
                for (int i = 0; i < size; ++i)
                {
                    original[i] = (byte)'A';
                }
            }

            if (getCrc)
            {
                Crc32 crc32 = new Crc32();
                crc32.Update(original, 0, size);
                crc = crc32.Value;
            }
        }


        public RuntimeInfo(string password, bool isDirectory)
        {
            this.method = CompressionMethod.Stored;
            this.compressionLevel = 1;
            this.password = password;
            this.size = 0;
            this.random = false;
            isDirectory_ = isDirectory;
            original = new byte[0];
        }

        public byte[] Original
        {
            get { return original; }
        }

        public CompressionMethod Method
        {
            get { return method; }
        }

        public int CompressionLevel
        {
            get { return compressionLevel; }
        }

        public int Size
        {
            get { return size; }
        }

        public string Password
        {
            get { return password; }
        }

        bool Random
        {
            get { return random; }
        }

        public long Crc
        {
            get { return crc; }
        }

        public bool IsDirectory
        {
            get { return isDirectory_; }
        }

        #region Instance Fields
        readonly byte[] original;
        readonly CompressionMethod method;
        int compressionLevel;
        int size;
        string password;
        bool random;
        bool isDirectory_;
        long crc = -1;
        #endregion
    }

    class MemoryDataSource : IStaticDataSource
    {
        #region Constructors
        /// <summary>
        /// Initialise a new instance.
        /// </summary>
        /// <param name="data">The data to provide.</param>
        public MemoryDataSource(byte[] data)
        {
            data_ = data;
        }
        #endregion

        #region IDataSource Members

        /// <summary>
        /// Get a Stream for this <see cref="IStaticDataSource"/>
        /// </summary>
        /// <returns>Returns a <see cref="Stream"/></returns>
        public Stream GetSource()
        {
            return new MemoryStream(data_);
        }
        #endregion

        #region Instance Fields
        readonly byte[] data_;
        #endregion
    }

    class StringMemoryDataSource : MemoryDataSource
    {
        public StringMemoryDataSource(string data)
            : base(Encoding.UTF8.GetBytes(data))
        {
        }
    }
    #endregion

    #region ZipBase
    public class ZipBase
    {
        

        protected byte[] MakeInMemoryZip(bool withSeek, params object[] createSpecs)
        {
            MemoryStream ms;

            if (withSeek)
            {
                ms = new MemoryStream();
            }
            else
            {
                ms = new MemoryStreamWithoutSeek();
            }

            using (ZipOutputStream outStream = new ZipOutputStream(ms))
            {
                for (int counter = 0; counter < createSpecs.Length; ++counter)
                {
                    RuntimeInfo info = createSpecs[counter] as RuntimeInfo;
                    outStream.Password = info.Password;

                    if (info.Method != CompressionMethod.Stored)
                    {
                        outStream.SetLevel(info.CompressionLevel); // 0 - store only to 9 - means best compression
                    }

                    string entryName;

                    if (info.IsDirectory)
                    {
                        entryName = "dir" + counter + "/";
                    }
                    else
                    {
                        entryName = "entry" + counter + ".tst";
                    }

                    ZipEntry entry = new ZipEntry(entryName);
                    entry.CompressionMethod = info.Method;
                    if (info.Crc >= 0)
                    {
                        entry.Crc = info.Crc;
                    }

                    outStream.PutNextEntry(entry);

                    if (info.Size > 0)
                    {
                        outStream.Write(info.Original, 0, info.Original.Length);
                    }
                }
            }
            return ms.ToArray();
        }

        protected byte[] MakeInMemoryZip(ref byte[] original, CompressionMethod method,
            int compressionLevel, int size, string password, bool withSeek)
        {
            MemoryStream ms;

            if (withSeek)
            {
                ms = new MemoryStream();
            }
            else
            {
                ms = new MemoryStreamWithoutSeek();
            }

            using (ZipOutputStream outStream = new ZipOutputStream(ms))
            {
                outStream.Password = password;

                if (method != CompressionMethod.Stored)
                {
                    outStream.SetLevel(compressionLevel); // 0 - store only to 9 - means best compression
                }

                ZipEntry entry = new ZipEntry("dummyfile.tst");
                entry.CompressionMethod = method;

                outStream.PutNextEntry(entry);

                if (size > 0)
                {
                    System.Random rnd = new Random();
                    original = new byte[size];
                    rnd.NextBytes(original);

                    // Although this could be written in one chunk doing it in lumps
                    // throws up buffering problems including with encryption the original
                    // source for this change.
                    int index = 0;
                    while (size > 0)
                    {
                        int count = (size > 0x200) ? 0x200 : size;
                        outStream.Write(original, index, count);
                        size -= 0x200;
                        index += count;
                    }
                }
            }
            return ms.ToArray();
        }

        

        protected static byte ScatterValue(byte rhs)
        {
            return (byte)((rhs * 253 + 7) & 0xff);
        }

        static void AddKnownDataToEntry(ZipOutputStream zipStream, int size)
        {
            if (size > 0)
            {
                byte nextValue = 0;
                int bufferSize = Math.Min(size, 65536);
                byte[] data = new byte[bufferSize];
                int currentIndex = 0;
                for (int i = 0; i < size; ++i)
                {
                    data[currentIndex] = nextValue;
                    nextValue = ScatterValue(nextValue);

                    currentIndex += 1;
                    if ((currentIndex >= data.Length) || (i + 1 == size))
                    {
                        zipStream.Write(data, 0, currentIndex);
                        currentIndex = 0;
                    }
                }
            }
        }

        protected void MakeZipFile(Stream storage, bool isOwner, string entryNamePrefix, int entries, int size, string comment)
        {
            using (ZipOutputStream zOut = new ZipOutputStream(storage))
            {
                zOut.IsStreamOwner = isOwner;
                zOut.SetComment(comment);
                for (int i = 0; i < entries; ++i)
                {
                    zOut.PutNextEntry(new ZipEntry(entryNamePrefix + (i + 1).ToString()));
                    AddKnownDataToEntry(zOut, size);
                }
            }
        }
         
        protected static void CheckKnownEntry(Stream inStream, int expectedCount)
        {
            byte[] buffer = new byte[1024];

            int bytesRead;
            int total = 0;
            byte nextValue = 0;
            while ((bytesRead = inStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += bytesRead;
                for (int i = 0; i < bytesRead; ++i)
                {
                    Assert.AreEqual(nextValue, buffer[i], "Wrong value read from entry");
                    nextValue = ScatterValue(nextValue);
                }
            }
            Assert.AreEqual(expectedCount, total, "Wrong number of bytes read from entry");
        }

        protected byte ReadByteChecked(Stream stream)
        {
            int rawValue = stream.ReadByte();
            Assert.IsTrue(rawValue >= 0);
            return (byte)rawValue;
        }

        protected int ReadInt(Stream stream)
        {
            return ReadByteChecked(stream) |
                (ReadByteChecked(stream) << 8) |
                (ReadByteChecked(stream) << 16) |
                (ReadByteChecked(stream) << 24);
        }

        protected long ReadLong(Stream stream)
        {
            long result = ReadInt(stream) & 0xffffffff;
            return result | (((long)ReadInt(stream)) << 32);
        }

    }

    #endregion

    class TestHelper
    {
        //static public void SaveMemoryStream(MemoryStream ms, string fileName)
        //{
        //    byte[] data = ms.ToArray();
        //    using (IsolatedStorageFileStream fs = store.OpenFile(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        //    {
        //        fs.Write(data, 0, data.Length);
        //    }
        //}

        static public int CompareDosDateTimes(DateTime l, DateTime r)
        {
            // Compare dates to dos accuracy...
            // Ticks can be different yet all these values are still the same!
            int result = l.Year - r.Year;
            if (result == 0)
            {
                result = l.Month - r.Month;
                if (result == 0)
                {
                    result = l.Day - r.Day;
                    if (result == 0)
                    {
                        result = l.Hour - r.Hour;
                        if (result == 0)
                        {
                            result = l.Minute - r.Minute;
                            if (result == 0)
                            {
                                result = (l.Second / 2) - (r.Second / 2);
                            }
                        }
                    }
                }
            }
            return result;
        }
    }

    [TestClass]
    public class ZipEntryHandling : ZipBase
    {
        byte[] MakeLocalHeader(string asciiName, short versionToExtract, short flags, short method,
                              int dostime, int crc, int compressedSize, int size)
        {
            using (TrackedMemoryStream ms = new TrackedMemoryStream())
            {
                ms.WriteByte((byte)'P');
                ms.WriteByte((byte)'K');
                ms.WriteByte(3);
                ms.WriteByte(4);

                ms.WriteLEShort(versionToExtract);
                ms.WriteLEShort(flags);
                ms.WriteLEShort(method);
                ms.WriteLEInt(dostime);
                ms.WriteLEInt(crc);
                ms.WriteLEInt(compressedSize);
                ms.WriteLEInt(size);

                byte[] rawName = Encoding.UTF8.GetBytes(asciiName);
                ms.WriteLEShort((short)rawName.Length);
                ms.WriteLEShort(0);
                ms.Write(rawName, 0, rawName.Length);
                return ms.ToArray();
            }
        }

        ZipEntry MakeEntry(string asciiName, short versionToExtract, short flags, short method,
                              int dostime, int crc, int compressedSize, int size)
        {
            byte[] data = MakeLocalHeader(asciiName, versionToExtract, flags, method,
                                          dostime, crc, compressedSize, size);

            ZipInputStream zis = new ZipInputStream(new MemoryStream(data));

            ZipEntry ze = zis.GetNextEntry();
            return ze;
        }

        



        /// <summary>
        /// Setting entry comments to null should be allowed
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void NullEntryComment()
        {
            ZipEntry test = new ZipEntry("null");
            test.Comment = null;
        }

        /// <summary>
        /// Entries with null names arent allowed
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        [ExpectedException(typeof(ArgumentNullException))]
        public void NullNameInConstructor()
        {
            string name = null;
            ZipEntry test = new ZipEntry(name);
        }

        [TestMethod]
        [Tag("Zip")]
        public void DateAndTime()
        {
            ZipEntry ze = new ZipEntry("Pok");

            // -1 is not strictly a valid MS-DOS DateTime value.
            // ZipEntry is lenient about handling invalid values.
            ze.DosTime = -1;

            Assert.AreEqual(new DateTime(2107, 12, 31, 23, 59, 59), ze.DateTime);

            // 0 is a special value meaning Now.
            ze.DosTime = 0;
            TimeSpan diff = DateTime.Now - ze.DateTime;

            // Value == 2 seconds!
            ze.DosTime = 1;
            Assert.AreEqual(new DateTime(1980, 1, 1, 0, 0, 2), ze.DateTime);

            // Over the limit are set to max.
            ze.DateTime = new DateTime(2108, 1, 1);
            Assert.AreEqual(new DateTime(2107, 12, 31, 23, 59, 58), ze.DateTime);

            // Under the limit are set to min.
            ze.DateTime = new DateTime(1906, 12, 4);
            Assert.AreEqual(new DateTime(1980, 1, 1, 0, 0, 0), ze.DateTime);
        }

        [TestMethod]
        [Tag("Zip")]
        public void DateTimeSetsDosTime()
        {
            ZipEntry ze = new ZipEntry("Pok");

            long original = ze.DosTime;

            ze.DateTime = new DateTime(1987, 9, 12);
            Assert.AreNotEqual(original, ze.DosTime);
            Assert.AreEqual(0, TestHelper.CompareDosDateTimes(new DateTime(1987, 9, 12), ze.DateTime));
        }

        [TestMethod]
        public void CanDecompress()
        {
            int dosTime = 12;
            int crc = 0xfeda;

            ZipEntry ze = MakeEntry("a", 10, 0, (short)CompressionMethod.Deflated,
                                    dosTime, crc, 1, 1);

            Assert.IsTrue(ze.CanDecompress);

            ze = MakeEntry("a", 45, 0, (short)CompressionMethod.Stored,
                                    dosTime, crc, 1, 1);
            Assert.IsTrue(ze.CanDecompress);

            ze = MakeEntry("a", 99, 0, (short)CompressionMethod.Deflated,
                                    dosTime, crc, 1, 1);
            Assert.IsFalse(ze.CanDecompress);
        }
    }

    /// <summary>
    /// This contains newer tests for stream handling. Much of this is still in GeneralHandling
    /// </summary>
    [TestClass]
    public class StreamHandling : ZipBase
    {
        void MustFailRead(Stream s, byte[] buffer, int offset, int count)
        {
            bool exception = false;
            try
            {
                s.Read(buffer, offset, count);
            }
            catch
            {
                exception = true;
            }
            Assert.IsTrue(exception, "Read should fail");
        }

        [TestMethod]
        [Tag("Zip")]
        public void ParameterHandling()
        {
            byte[] buffer = new byte[10];
            byte[] emptyBuffer = new byte[0];

            MemoryStream ms = new MemoryStream();
            ZipOutputStream outStream = new ZipOutputStream(ms);
            outStream.IsStreamOwner = false;
            outStream.PutNextEntry(new ZipEntry("Floyd"));
            outStream.Write(buffer, 0, 10);
            outStream.Finish();

            ms.Seek(0, SeekOrigin.Begin);

            ZipInputStream inStream = new ZipInputStream(ms);
            ZipEntry e = inStream.GetNextEntry();

            MustFailRead(inStream, null, 0, 0);
            MustFailRead(inStream, buffer, -1, 1);
            MustFailRead(inStream, buffer, 0, 11);
            MustFailRead(inStream, buffer, 7, 5);
            MustFailRead(inStream, buffer, 0, -1);

            MustFailRead(inStream, emptyBuffer, 0, 1);

            int bytesRead = inStream.Read(buffer, 10, 0);
            Assert.AreEqual(0, bytesRead, "Should be able to read zero bytes");

            bytesRead = inStream.Read(emptyBuffer, 0, 0);
            Assert.AreEqual(0, bytesRead, "Should be able to read zero bytes");
        }

        /// <summary>
        /// Check that Zip64 descriptor is added to an entry OK.
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void Zip64Descriptor()
        {
            MemoryStream msw = new MemoryStreamWithoutSeek();
            ZipOutputStream outStream = new ZipOutputStream(msw);
            outStream.UseZip64 = UseZip64.Off;

            outStream.IsStreamOwner = false;
            outStream.PutNextEntry(new ZipEntry("StripedMarlin"));
            outStream.WriteByte(89);
            outStream.Close();

            Assert.IsTrue(ICSharpCode.SharpZipLib.Tests.TestSupport.ZipTesting.TestArchive(msw.ToArray()));

            msw = new MemoryStreamWithoutSeek();
            outStream = new ZipOutputStream(msw);
            outStream.UseZip64 = UseZip64.On;

            outStream.IsStreamOwner = false;
            outStream.PutNextEntry(new ZipEntry("StripedMarlin"));
            outStream.WriteByte(89);
            outStream.Close();

            Assert.IsTrue(ICSharpCode.SharpZipLib.Tests.TestSupport.ZipTesting.TestArchive(msw.ToArray()));
        }

        [TestMethod]
        [Tag("Zip")]
        public void ReadAndWriteZip64NonSeekable()
        {
            MemoryStream msw = new MemoryStreamWithoutSeek();
            using (ZipOutputStream outStream = new ZipOutputStream(msw))
            {
                outStream.UseZip64 = UseZip64.On;

                outStream.IsStreamOwner = false;
                outStream.PutNextEntry(new ZipEntry("StripedMarlin"));
                outStream.WriteByte(89);

                outStream.PutNextEntry(new ZipEntry("StripedMarlin2"));
                outStream.WriteByte(89);

                outStream.Close();
            }

            Assert.IsTrue(ICSharpCode.SharpZipLib.Tests.TestSupport.ZipTesting.TestArchive(msw.ToArray()));

            msw.Position = 0;

            using (ZipInputStream zis = new ZipInputStream(msw))
            {
                while (zis.GetNextEntry() != null)
                {
                    int len = 0;
                    int bufferSize = 1024;
                    byte[] buffer = new byte[bufferSize];
                    while ((len = zis.Read(buffer, 0, bufferSize)) > 0)
                    {
                        // Reading the data is enough
                    }
                }
            }
        }

        /// <summary>
        /// Check that adding an entry with no data and Zip64 works OK
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void EntryWithNoDataAndZip64()
        {
            MemoryStream msw = new MemoryStreamWithoutSeek();
            ZipOutputStream outStream = new ZipOutputStream(msw);

            outStream.IsStreamOwner = false;
            ZipEntry ze = new ZipEntry("Striped Marlin");
            ze.ForceZip64();
            ze.Size = 0;
            outStream.PutNextEntry(ze);
            outStream.CloseEntry();
            outStream.Finish();
            outStream.Close();

            Assert.IsTrue(ICSharpCode.SharpZipLib.Tests.TestSupport.ZipTesting.TestArchive(msw.ToArray()));
        }
        /// <summary>
        /// Empty zip entries can be created and read?
        /// </summary>

        [TestMethod]
        [Tag("Zip")]
        public void EmptyZipEntries()
        {
            MemoryStream ms = new MemoryStream();
            ZipOutputStream outStream = new ZipOutputStream(ms);

            for (int i = 0; i < 10; ++i)
            {
                outStream.PutNextEntry(new ZipEntry(i.ToString()));
            }

            outStream.Finish();

            ms.Seek(0, SeekOrigin.Begin);

            ZipInputStream inStream = new ZipInputStream(ms);

            int extractCount = 0;
            byte[] decompressedData = new byte[100];

            while ((inStream.GetNextEntry()) != null)
            {
                while (true)
                {
                    int numRead = inStream.Read(decompressedData, extractCount, decompressedData.Length);
                    if (numRead <= 0)
                    {
                        break;
                    }
                    extractCount += numRead;
                }
            }
            inStream.Close();
            Assert.AreEqual(extractCount, 0, "No data should be read from empty entries");
        }

        /// <summary>
        /// Empty zips can be created and read?
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void CreateAndReadEmptyZip()
        {
            MemoryStream ms = new MemoryStream();
            ZipOutputStream outStream = new ZipOutputStream(ms);
            outStream.Finish();

            ms.Seek(0, SeekOrigin.Begin);

            ZipInputStream inStream = new ZipInputStream(ms);
            while ((inStream.GetNextEntry()) != null)
            {
                Assert.Fail("No entries should be found in empty zip");
            }
        }

        /// <summary>
        /// Base stream is closed when IsOwner is true ( default);
        /// </summary>
        [TestMethod]
        public void BaseClosedWhenOwner()
        {
            TrackedMemoryStream ms = new TrackedMemoryStream();

            Assert.IsFalse(ms.IsClosed, "Underlying stream should NOT be closed");

            using (ZipOutputStream stream = new ZipOutputStream(ms))
            {
                Assert.IsTrue(stream.IsStreamOwner, "Should be stream owner by default");
            }

            Assert.IsTrue(ms.IsClosed, "Underlying stream should be closed");
        }

        /// <summary>
        /// Check that base stream is not closed when IsOwner is false;
        /// </summary>
        [TestMethod]
        public void BaseNotClosedWhenNotOwner()
        {
            TrackedMemoryStream ms = new TrackedMemoryStream();

            Assert.IsFalse(ms.IsClosed, "Underlying stream should NOT be closed");

            using (ZipOutputStream stream = new ZipOutputStream(ms))
            {
                Assert.IsTrue(stream.IsStreamOwner, "Should be stream owner by default");
                stream.IsStreamOwner = false;
            }
            Assert.IsFalse(ms.IsClosed, "Underlying stream should still NOT be closed");
        }

        /// <summary>
        /// Check that base stream is not closed when IsOwner is false;
        /// </summary>
        [TestMethod]
        public void BaseClosedAfterFailure()
        {
            TrackedMemoryStream ms = new TrackedMemoryStream(new byte[32]);

            Assert.IsFalse(ms.IsClosed, "Underlying stream should NOT be closed initially");
            bool blewUp = false;
            try
            {
                using (ZipOutputStream stream = new ZipOutputStream(ms))
                {
                    Assert.IsTrue(stream.IsStreamOwner, "Should be stream owner by default");
                    try
                    {
                        stream.PutNextEntry(new ZipEntry("Tiny"));
                        stream.Write(new byte[32], 0, 32);
                    }
                    finally
                    {
                        Assert.IsFalse(ms.IsClosed, "Stream should still not be closed.");
                        stream.Close();
                        Assert.Fail("Exception not thrown");
                    }
                }
            }
            catch
            {
                blewUp = true;
            }

            Assert.IsTrue(blewUp, "Should have failed to write to stream");
            Assert.IsTrue(ms.IsClosed, "Underlying stream should be closed");
        }

        [TestMethod]
        [Tag("Zip")]
        public void WriteThroughput()
        {
            outStream_ = new ZipOutputStream(new NullStream());

            DateTime startTime = DateTime.Now;

            long target = 0x10000000;

            writeTarget_ = target;
            outStream_.PutNextEntry(new ZipEntry("0"));
            WriteTargetBytes();

            outStream_.Close();

            DateTime endTime = DateTime.Now;
            TimeSpan span = endTime - startTime;
            Console.WriteLine("Time {0} throughput {1} KB/Sec", span, (target / 1024.0) / span.TotalSeconds);
        }

        [TestMethod/*,Ignore("Long Running")*/]
        [Tag("Zip")]
        [Tag("Long Running")]
        public void SingleLargeEntry()
        {
            window_ = new WindowedStream(0x10000);
            outStream_ = new ZipOutputStream(window_);
            inStream_ = new ZipInputStream(window_);

            long target = 0x10000000;
            readTarget_ = writeTarget_ = target;

            Thread reader = new Thread(Reader);
            reader.Name = "Reader";

            Thread writer = new Thread(Writer);
            writer.Name = "Writer";

            DateTime startTime = DateTime.Now;
            reader.Start();
            writer.Start();

            writer.Join();
            reader.Join();

            DateTime endTime = DateTime.Now;
            TimeSpan span = endTime - startTime;
            Console.WriteLine("Time {0} throughput {1} KB/Sec", span, (target / 1024.0) / span.TotalSeconds);
        }

        void Reader()
        {
            const int Size = 8192;
            int readBytes = 1;
            byte[] buffer = new byte[Size];

            long passifierLevel = readTarget_ - 0x10000000;
            ZipEntry single = inStream_.GetNextEntry();

            Assert.AreEqual(single.Name, "CantSeek");
            Assert.IsTrue((single.Flags & (int)GeneralBitFlags.Descriptor) != 0);

            while ((readTarget_ > 0) && (readBytes > 0))
            {
                int count = Size;
                if (count > readTarget_)
                {
                    count = (int)readTarget_;
                }

                readBytes = inStream_.Read(buffer, 0, count);
                readTarget_ -= readBytes;

                if (readTarget_ <= passifierLevel)
                {
                    Console.WriteLine("Reader {0} bytes remaining", readTarget_);
                    passifierLevel = readTarget_ - 0x10000000;
                }
            }

            Assert.IsTrue(window_.IsClosed, "Window should be closed");

            // This shouldnt read any data but should read the footer
            readBytes = inStream_.Read(buffer, 0, 1);
            Assert.AreEqual(0, readBytes, "Stream should be empty");
            Assert.AreEqual(0, window_.Length, "Window should be closed");
            inStream_.Close();
        }

        void WriteTargetBytes()
        {
            const int Size = 8192;

            byte[] buffer = new byte[Size];

            while (writeTarget_ > 0)
            {
                int thisTime = Size;
                if (thisTime > writeTarget_)
                {
                    thisTime = (int)writeTarget_;
                }

                outStream_.Write(buffer, 0, thisTime);
                writeTarget_ -= thisTime;
            }
        }

        void Writer()
        {
            outStream_.PutNextEntry(new ZipEntry("CantSeek"));
            WriteTargetBytes();
            outStream_.Close();
        }

        WindowedStream window_;
        ZipOutputStream outStream_;
        ZipInputStream inStream_;
        long readTarget_;
        long writeTarget_;

    }

    public class TransformBase : ZipBase
    {
        protected void TestFile(INameTransform t, string original, string expected)
        {
            string transformed = t.TransformFile(original);
            Assert.AreEqual(expected, transformed, "Should be equal");
        }

        protected void TestDirectory(INameTransform t, string original, string expected)
        {
            string transformed = t.TransformDirectory(original);
            Assert.AreEqual(expected, transformed, "Should be equal");
        }
    }

    [TestClass]
    public class WindowsNameTransformHandling : TransformBase
    {
        [TestMethod]
        public void BasicFiles()
        {
            WindowsNameTransform wnt = new WindowsNameTransform();
            wnt.TrimIncomingPaths = false;

            TestFile(wnt, "Bogan", "Bogan");
            TestFile(wnt, "absolute/file2", @"absolute\file2");
            TestFile(wnt, "C:/base/////////t", @"base\t");
            TestFile(wnt, "//unc/share/zebidi/and/dylan", @"zebidi\and\dylan");
            TestFile(wnt, @"\\unc\share\/zebidi\/and\/dylan", @"zebidi\and\dylan");
        }

        [TestMethod]
        public void Replacement()
        {
            WindowsNameTransform wnt = new WindowsNameTransform();
            wnt.TrimIncomingPaths = false;

            TestFile(wnt, "c::", "_");
            TestFile(wnt, "c\\/>", @"c\_");
        }

        [TestMethod]
        public void NameTooLong()
        {
            WindowsNameTransform wnt = new WindowsNameTransform();
            string veryLong = new string('x', 261);
            try
            {
                wnt.TransformDirectory(veryLong);
                Assert.Fail("Expected an exception");
            }
            catch (PathTooLongException)
            {
            }
        }

        [TestMethod]
        public void LengthBoundaryOk()
        {
            WindowsNameTransform wnt = new WindowsNameTransform();
            string veryLong = "c:\\" + new string('x', 260);
            try
            {
                string transformed = wnt.TransformDirectory(veryLong);
            }
            catch
            {
                Assert.Fail("Expected no exception");
            }
        }

        [TestMethod]
        public void ReplacementChecking()
        {
            WindowsNameTransform wnt = new WindowsNameTransform();
            try
            {
                wnt.Replacement = '*';
                Assert.Fail("Expected an exception");
            }
            catch (ArgumentException)
            {
            }

            try
            {
                wnt.Replacement = '?';
                Assert.Fail("Expected an exception");
            }
            catch (ArgumentException)
            {
            }

            try
            {
                wnt.Replacement = ':';
                Assert.Fail("Expected an exception");
            }
            catch (ArgumentException)
            {
            }

            try
            {
                wnt.Replacement = '/';
                Assert.Fail("Expected an exception");
            }
            catch (ArgumentException)
            {
            }

            try
            {
                wnt.Replacement = '\\';
                Assert.Fail("Expected an exception");
            }
            catch (ArgumentException)
            {
            }
        }


    }

    [TestClass]
    public class ZipNameTransformHandling : TransformBase
    {
        [TestMethod]
        [Tag("Zip")]
        public void Basic()
        {
            ZipNameTransform t = new ZipNameTransform();

            TestFile(t, "abcdef", "abcdef");
            TestFile(t, @"\\uncpath\d1\file1", "file1");
            TestFile(t, @"C:\absolute\file2", "absolute/file2");

            // This is ignored but could be converted to 'file3'
            TestFile(t, @"./file3", "./file3");

            // The following relative paths cant be handled and are ignored
            TestFile(t, @"../file3", "../file3");
            TestFile(t, @".../file3", ".../file3");

            // Trick filenames.
            TestFile(t, @".....file3", ".....file3");
            TestFile(t, @"c::file", "_file");
        }

        [TestMethod]
        public void TooLong()
        {
            ZipNameTransform zt = new ZipNameTransform();
            string veryLong = new string('x', 65536);
            try
            {
                zt.TransformDirectory(veryLong);
                Assert.Fail("Expected an exception");
            }
            catch (PathTooLongException)
            {
            }
        }

        [TestMethod]
        public void LengthBoundaryOk()
        {
            ZipNameTransform zt = new ZipNameTransform();
            string veryLong = "c:\\" + new string('x', 65535);
            try
            {
                zt.TransformDirectory(veryLong);
            }
            catch
            {
                Assert.Fail("Expected no exception");
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void NameTransforms()
        {
            INameTransform t = new ZipNameTransform(@"C:\Slippery");
            Assert.AreEqual("Pongo/Directory/", t.TransformDirectory(@"C:\Slippery\Pongo\Directory"), "Value should be trimmed and converted");
            Assert.AreEqual("PoNgo/Directory/", t.TransformDirectory(@"c:\slipperY\PoNgo\Directory"), "Trimming should be case insensitive");
            Assert.AreEqual("slippery/Pongo/Directory/", t.TransformDirectory(@"d:\slippery\Pongo\Directory"), "Trimming should be case insensitive");

            Assert.AreEqual("Pongo/File", t.TransformFile(@"C:\Slippery\Pongo\File"), "Value should be trimmed and converted");
        }

        /// <summary>
        /// Test ZipEntry static file name cleaning methods
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void FilenameCleaning()
        {
            Assert.AreEqual(0, string.Compare(ZipEntry.CleanName("hello"), "hello"));
            Assert.AreEqual(0, string.Compare(ZipEntry.CleanName(@"z:\eccles"), "eccles"));
            Assert.AreEqual(0, string.Compare(ZipEntry.CleanName(@"\\server\share\eccles"), "eccles"));
            Assert.AreEqual(0, string.Compare(ZipEntry.CleanName(@"\\server\share\dir\eccles"), "dir/eccles"));
        }

        [TestMethod]
        [Tag("Zip")]
        public void PathalogicalNames()
        {
            string badName = ".*:\\zy3$";

            Assert.IsFalse(ZipNameTransform.IsValidName(badName));

            ZipNameTransform t = new ZipNameTransform();
            string result = t.TransformFile(badName);

            Assert.IsTrue(ZipNameTransform.IsValidName(result));
        }
    }

    /// <summary>
    /// This class contains test cases for Zip compression and decompression
    /// </summary>
    [TestClass]
    public class GeneralHandling : ZipBase
    {
        void AddRandomDataToEntry(ZipOutputStream zipStream, int size)
        {
            if (size > 0)
            {
                byte[] data = new byte[size];
                System.Random rnd = new Random();
                rnd.NextBytes(data);

                zipStream.Write(data, 0, data.Length);
            }
        }

        void ExerciseZip(CompressionMethod method, int compressionLevel,
            int size, string password, bool canSeek)
        {
            byte[] originalData = null;
            byte[] compressedData = MakeInMemoryZip(ref originalData, method, compressionLevel, size, password, canSeek);

            MemoryStream ms = new MemoryStream(compressedData);
            ms.Seek(0, SeekOrigin.Begin);

            using (ZipInputStream inStream = new ZipInputStream(ms))
            {
                byte[] decompressedData = new byte[size];
                if (password != null)
                {
                    inStream.Password = password;
                }

                ZipEntry entry2 = inStream.GetNextEntry();

                if ((entry2.Flags & 8) == 0)
                {
                    Assert.AreEqual(size, entry2.Size, "Entry size invalid");
                }

                int currentIndex = 0;

                if (size > 0)
                {
                    int count = decompressedData.Length;

                    while (true)
                    {
                        int numRead = inStream.Read(decompressedData, currentIndex, count);
                        if (numRead <= 0)
                        {
                            break;
                        }
                        currentIndex += numRead;
                        count -= numRead;
                    }
                }

                Assert.AreEqual(currentIndex, size, "Original and decompressed data different sizes");

                if (originalData != null)
                {
                    for (int i = 0; i < originalData.Length; ++i)
                    {
                        Assert.AreEqual(decompressedData[i], originalData[i], "Decompressed data doesnt match original, compression level: " + compressionLevel);
                    }
                }
            }
        }

        string DescribeAttributes(FieldAttributes attributes)
        {
            string att = string.Empty;
            if ((FieldAttributes.Public & attributes) != 0)
            {
                att = att + "Public,";
            }

            if ((FieldAttributes.Static & attributes) != 0)
            {
                att = att + "Static,";
            }

            if ((FieldAttributes.Literal & attributes) != 0)
            {
                att = att + "Literal,";
            }

            if ((FieldAttributes.HasDefault & attributes) != 0)
            {
                att = att + "HasDefault,";
            }

            if ((FieldAttributes.InitOnly & attributes) != 0)
            {
                att = att + "InitOnly,";
            }

            if ((FieldAttributes.Assembly & attributes) != 0)
            {
                att = att + "Assembly,";
            }

            if ((FieldAttributes.FamANDAssem & attributes) != 0)
            {
                att = att + "FamANDAssembly,";
            }

            if ((FieldAttributes.FamORAssem & attributes) != 0)
            {
                att = att + "FamORAssembly,";
            }

            if ((FieldAttributes.HasFieldMarshal & attributes) != 0)
            {
                att = att + "HasFieldMarshal,";
            }

            return att;
        }

        [TestMethod]
        [Tag("Zip")]
        [ExpectedException(typeof(NotSupportedException))]
        public void UnsupportedCompressionMethod()
        {
            ZipEntry ze = new ZipEntry("HumblePie");
            ze.CompressionMethod = CompressionMethod.BZip2;
        }

        /// <summary>
        /// Invalid passwords should be detected early if possible, seekable stream
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        [ExpectedException(typeof(ZipException))]
        public void InvalidPasswordSeekable()
        {
            byte[] originalData = null;
            byte[] compressedData = MakeInMemoryZip(ref originalData, CompressionMethod.Deflated, 3, 500, "Hola", true);

            MemoryStream ms = new MemoryStream(compressedData);
            ms.Seek(0, SeekOrigin.Begin);

            byte[] buf2 = new byte[originalData.Length];
            int pos = 0;

            ZipInputStream inStream = new ZipInputStream(ms);
            inStream.Password = "redhead";

            ZipEntry entry2 = inStream.GetNextEntry();

            while (true)
            {
                int numRead = inStream.Read(buf2, pos, buf2.Length);
                if (numRead <= 0)
                {
                    break;
                }
                pos += numRead;
            }
        }

        /// <summary>
        /// Check that GetNextEntry can handle the situation where part of the entry data has been read
        /// before the call is made.  ZipInputStream.CloseEntry wasnt handling this at all.
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void ExerciseGetNextEntry()
        {
            byte[] compressedData = MakeInMemoryZip(
                true,
                new RuntimeInfo(CompressionMethod.Deflated, 9, 50, null, true),
                new RuntimeInfo(CompressionMethod.Deflated, 2, 50, null, true),
                new RuntimeInfo(CompressionMethod.Deflated, 9, 50, null, true),
                new RuntimeInfo(CompressionMethod.Deflated, 2, 50, null, true),
                new RuntimeInfo(null, true),
                new RuntimeInfo(CompressionMethod.Stored, 2, 50, null, true),
                new RuntimeInfo(CompressionMethod.Deflated, 9, 50, null, true)
                );

            MemoryStream ms = new MemoryStream(compressedData);
            ms.Seek(0, SeekOrigin.Begin);

            using (ZipInputStream inStream = new ZipInputStream(ms))
            {
                byte[] buffer = new byte[10];

                while (inStream.GetNextEntry() != null)
                {
                    // Read a portion of the data, so GetNextEntry has some work to do.
                    inStream.Read(buffer, 0, 10);
                }
            }
        }

        /// <summary>
        /// Invalid passwords should be detected early if possible, non seekable stream
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        [ExpectedException(typeof(ZipException))]
        public void InvalidPasswordNonSeekable()
        {
            byte[] originalData = null;
            byte[] compressedData = MakeInMemoryZip(ref originalData, CompressionMethod.Deflated, 3, 500, "Hola", false);

            MemoryStream ms = new MemoryStream(compressedData);
            ms.Seek(0, SeekOrigin.Begin);

            byte[] buf2 = new byte[originalData.Length];
            int pos = 0;

            ZipInputStream inStream = new ZipInputStream(ms);
            inStream.Password = "redhead";

            ZipEntry entry2 = inStream.GetNextEntry();

            while (true)
            {
                int numRead = inStream.Read(buf2, pos, buf2.Length);
                if (numRead <= 0)
                {
                    break;
                }
                pos += numRead;
            }
        }

        /// <summary>
        /// Adding an entry after the stream has Finished should fail
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddEntryAfterFinish()
        {
            MemoryStream ms = new MemoryStream();
            ZipOutputStream s = new ZipOutputStream(ms);
            s.Finish();
            s.PutNextEntry(new ZipEntry("dummyfile.tst"));
        }

        /// <summary>
        /// Test setting file commment to a value that is too long
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void SetCommentOversize()
        {
            MemoryStream ms = new MemoryStream();
            ZipOutputStream s = new ZipOutputStream(ms);
            s.SetComment(new String('A', 65536));
        }

        /// <summary>
        /// Check that simply closing ZipOutputStream finishes the zip correctly
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void CloseOnlyHandled()
        {
            MemoryStream ms = new MemoryStream();
            ZipOutputStream s = new ZipOutputStream(ms);
            s.PutNextEntry(new ZipEntry("dummyfile.tst"));
            s.Close();

            Assert.IsTrue(s.IsFinished, "Output stream should be finished");
        }

        /// <summary>
        /// Basic compress/decompress test, no encryption, size is important here as its big enough
        /// to force multiple write to output which was a problem...
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicDeflated()
        {
            for (int i = 0; i <= 9; ++i)
            {
                ExerciseZip(CompressionMethod.Deflated, i, 50000, null, true);
            }
        }

        /// <summary>
        /// Basic compress/decompress test, no encryption, size is important here as its big enough
        /// to force multiple write to output which was a problem...
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicDeflatedNonSeekable()
        {
            for (int i = 0; i <= 9; ++i)
            {
                ExerciseZip(CompressionMethod.Deflated, i, 50000, null, false);
            }
        }

        /// <summary>
        /// Basic stored file test, no encryption.
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicStored()
        {
            ExerciseZip(CompressionMethod.Stored, 0, 50000, null, true);
        }

        /// <summary>
        /// Basic stored file test, no encryption, non seekable output
        /// NOTE this gets converted to deflate level 0
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicStoredNonSeekable()
        {
            ExerciseZip(CompressionMethod.Stored, 0, 50000, null, false);
        }

        [TestMethod]
        [Tag("Zip")]
        public void StoredNonSeekableKnownSizeNoCrc()
        {
            // This cannot be stored directly as the crc is not be known.
            const int TargetSize = 21348;
            const string Password = null;

            MemoryStream ms = new MemoryStreamWithoutSeek();

            using (ZipOutputStream outStream = new ZipOutputStream(ms))
            {
                outStream.Password = Password;
                outStream.IsStreamOwner = false;
                ZipEntry entry = new ZipEntry("dummyfile.tst");
                entry.CompressionMethod = CompressionMethod.Stored;

                // The bit thats in question is setting the size before its added to the archive.
                entry.Size = TargetSize;

                outStream.PutNextEntry(entry);

                Assert.AreEqual(CompressionMethod.Deflated, entry.CompressionMethod, "Entry should be deflated");
                Assert.AreEqual(-1, entry.CompressedSize, "Compressed size should be known");

                System.Random rnd = new Random();

                int size = TargetSize;
                byte[] original = new byte[size];
                rnd.NextBytes(original);

                // Although this could be written in one chunk doing it in lumps
                // throws up buffering problems including with encryption the original
                // source for this change.
                int index = 0;
                while (size > 0)
                {
                    int count = (size > 0x200) ? 0x200 : size;
                    outStream.Write(original, index, count);
                    size -= 0x200;
                    index += count;
                }
            }
            Assert.IsTrue(ICSharpCode.SharpZipLib.Tests.TestSupport.ZipTesting.TestArchive(ms.ToArray()));
        }

        [TestMethod]
        [Tag("Zip")]
        public void StoredNonSeekableKnownSizeNoCrcEncrypted()
        {
            // This cant be stored directly as the crc is not known
            const int TargetSize = 24692;
            const string Password = "Mabutu";

            MemoryStream ms = new MemoryStreamWithoutSeek();

            using (ZipOutputStream outStream = new ZipOutputStream(ms))
            {
                outStream.Password = Password;
                outStream.IsStreamOwner = false;
                ZipEntry entry = new ZipEntry("dummyfile.tst");
                entry.CompressionMethod = CompressionMethod.Stored;

                // The bit thats in question is setting the size before its added to the archive.
                entry.Size = TargetSize;

                outStream.PutNextEntry(entry);

                Assert.AreEqual(CompressionMethod.Deflated, entry.CompressionMethod, "Entry should be stored");
                Assert.AreEqual(-1, entry.CompressedSize, "Compressed size should be known");

                System.Random rnd = new Random();

                int size = TargetSize;
                byte[] original = new byte[size];
                rnd.NextBytes(original);

                // Although this could be written in one chunk doing it in lumps
                // throws up buffering problems including with encryption the original
                // source for this change.
                int index = 0;
                while (size > 0)
                {
                    int count = (size > 0x200) ? 0x200 : size;
                    outStream.Write(original, index, count);
                    size -= 0x200;
                    index += count;
                }
            }
            Assert.IsTrue(ICSharpCode.SharpZipLib.Tests.TestSupport.ZipTesting.TestArchive(ms.ToArray(), Password));
        }

        /// <summary>
        /// Basic compress/decompress test, with encryption, size is important here as its big enough
        /// to force multiple writes to output which was a problem...
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicDeflatedEncrypted()
        {
            for (int i = 0; i <= 9; ++i)
            {
                ExerciseZip(CompressionMethod.Deflated, i, 50157, "Rosebud", true);
            }
        }

        /// <summary>
        /// Basic compress/decompress test, with encryption, size is important here as its big enough
        /// to force multiple write to output which was a problem...
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicDeflatedEncryptedNonSeekable()
        {
            for (int i = 0; i <= 9; ++i)
            {
                ExerciseZip(CompressionMethod.Deflated, i, 50000, "Rosebud", false);
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void SkipEncryptedEntriesWithoutSettingPassword()
        {
            byte[] compressedData = MakeInMemoryZip(true,
                new RuntimeInfo("1234", true),
                new RuntimeInfo(CompressionMethod.Deflated, 2, 1, null, true),
                new RuntimeInfo(CompressionMethod.Deflated, 9, 1, "1234", true),
                new RuntimeInfo(CompressionMethod.Deflated, 2, 1, null, true),
                new RuntimeInfo(null, true),
                new RuntimeInfo(CompressionMethod.Stored, 2, 1, "4321", true),
                new RuntimeInfo(CompressionMethod.Deflated, 9, 1, "1234", true)
                );

            MemoryStream ms = new MemoryStream(compressedData);
            ZipInputStream inStream = new ZipInputStream(ms);

            while (inStream.GetNextEntry() != null)
            {
            }

            inStream.Close();
        }

        [TestMethod]
        [Tag("Zip")]
        public void MixedEncryptedAndPlain()
        {
            byte[] compressedData = MakeInMemoryZip(true,
                new RuntimeInfo(CompressionMethod.Deflated, 2, 1, null, true),
                new RuntimeInfo(CompressionMethod.Deflated, 9, 1, "1234", false),
                new RuntimeInfo(CompressionMethod.Deflated, 2, 1, null, false),
                new RuntimeInfo(CompressionMethod.Deflated, 9, 1, "1234", true)
                );

            MemoryStream ms = new MemoryStream(compressedData);
            using (ZipInputStream inStream = new ZipInputStream(ms))
            {
                inStream.Password = "1234";

                int extractCount = 0;
                int extractIndex = 0;
                ZipEntry entry;
                byte[] decompressedData = new byte[100];

                while ((entry = inStream.GetNextEntry()) != null)
                {
                    extractCount = decompressedData.Length;
                    extractIndex = 0;
                    while (true)
                    {
                        int numRead = inStream.Read(decompressedData, extractIndex, extractCount);
                        if (numRead <= 0)
                        {
                            break;
                        }
                        extractIndex += numRead;
                        extractCount -= numRead;
                    }
                }
                inStream.Close();
            }
        }

        /// <summary>
        /// Basic stored file test, with encryption.
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicStoredEncrypted()
        {
            ExerciseZip(CompressionMethod.Stored, 0, 50000, "Rosebud", true);
        }

        /// <summary>
        /// Basic stored file test, with encryption, non seekable output.
        /// NOTE this gets converted deflate level 0
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void BasicStoredEncryptedNonSeekable()
        {
            ExerciseZip(CompressionMethod.Stored, 0, 50000, "Rosebud", false);
        }

        /// <summary>
        /// Check that when the output stream cannot seek that requests for stored
        /// are in fact converted to defalted level 0
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void StoredNonSeekableConvertToDeflate()
        {
            MemoryStreamWithoutSeek ms = new MemoryStreamWithoutSeek();

            ZipOutputStream outStream = new ZipOutputStream(ms);
            outStream.SetLevel(8);
            Assert.AreEqual(8, outStream.GetLevel(), "Compression level invalid");

            ZipEntry entry = new ZipEntry("1.tst");
            entry.CompressionMethod = CompressionMethod.Stored;
            outStream.PutNextEntry(entry);
            Assert.AreEqual(0, outStream.GetLevel(), "Compression level invalid");

            AddRandomDataToEntry(outStream, 100);
            entry = new ZipEntry("2.tst");
            entry.CompressionMethod = CompressionMethod.Deflated;
            outStream.PutNextEntry(entry);
            Assert.AreEqual(8, outStream.GetLevel(), "Compression level invalid");
            AddRandomDataToEntry(outStream, 100);

            outStream.Close();
        }

        /// <summary>
        /// Check that adding more than the 2.0 limit for entry numbers is detected and handled
        /// </summary>
        [TestMethod/*,Ignore("Long Running")*/]
        [Tag("Zip")]
        [Tag("Long Running")]
        public void Stream_64KPlusOneEntries()
        {
            const int target = 65537;
            MemoryStream ms = new MemoryStream();
            using (ZipOutputStream s = new ZipOutputStream(ms))
            {

                for (int i = 0; i < target; ++i)
                {
                    s.PutNextEntry(new ZipEntry("dummyfile.tst"));
                }

                s.Finish();
                ms.Seek(0, SeekOrigin.Begin);
                using (ZipFile zipFile = new ZipFile(ms))
                {
                    Assert.AreEqual(target, zipFile.Count, "Incorrect number of entries stored");
                }
            }
        }

        /// <summary>
        /// Check that Unicode filename support works.
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void Stream_UnicodeEntries()
        {
            MemoryStream ms = new MemoryStream();
            using (ZipOutputStream s = new ZipOutputStream(ms))
            {
                s.IsStreamOwner = false;

                string sampleName = "\u03A5\u03d5\u03a3";
                ZipEntry sample = new ZipEntry(sampleName);
                sample.IsUnicodeText = true;
                s.PutNextEntry(sample);

                s.Finish();
                ms.Seek(0, SeekOrigin.Begin);

                using (ZipInputStream zis = new ZipInputStream(ms))
                {
                    ZipEntry ze = zis.GetNextEntry();
                    Assert.AreEqual(sampleName, ze.Name, "Expected name to match original");
                    Assert.IsTrue(ze.IsUnicodeText, "Expected IsUnicodeText flag to be set");
                }
            }
        }



        void TestLargeZip(string tempFile, int targetFiles)
        {
            const int BlockSize = 4096;

            byte[] data = new byte[BlockSize];
            byte nextValue = 0;
            for (int i = 0; i < BlockSize; ++i)
            {
                nextValue = ScatterValue(nextValue);
                data[i] = nextValue;
            }

            using (ZipFile zFile = new ZipFile(tempFile))
            {
                Assert.AreEqual(targetFiles, zFile.Count);
                byte[] readData = new byte[BlockSize];
                int readIndex;
                foreach (ZipEntry ze in zFile)
                {
                    Stream s = zFile.GetInputStream(ze);
                    readIndex = 0;
                    while (readIndex < readData.Length)
                    {
                        readIndex += s.Read(readData, readIndex, data.Length - readIndex);
                    }

                    for (int ii = 0; ii < BlockSize; ++ii)
                    {
                        Assert.AreEqual(data[ii], readData[ii]);
                    }
                }
                zFile.Close();
            }
        }

        //      [TestMethod]
        //      [Tag("Zip")]
        //      [Tag("CreatesTempFile")]
        public void TestLargeZipFile()
        {
            string tempFile = @"g:\\tmp";
            tempFile = Path.Combine(tempFile, "SharpZipTest.Zip");
            TestLargeZip(tempFile, 8100);
        }

        

        ///// <summary>
        ///// Test for handling of zero lengths in compression using a formatter which
        ///// will request reads of zero length...
        ///// </summary>
        //[TestMethod]
        //[Tag("Zip")]
        //public void SerializedObjectZeroLength()
        //{
        //    object data = new byte[0];
        //    // Thisa wont be zero length here due to serialisation.
        //    byte[] zipped = ZipZeroLength(data);
        //    object o = UnZipZeroLength(zipped);

        //    byte[] returned = o as byte[];

        //    Assert.IsNotNull(returned, "Expected a byte[]");
        //    Assert.AreEqual(0, returned.Length);
        //}

        ///// <summary>
        ///// Test for handling of serialized reference and value objects.
        ///// </summary>
        //[TestMethod]
        //[Tag("Zip")]
        //public void SerializedObject()
        //{
        //    DateTime sampleDateTime = new DateTime(1853, 8, 26);
        //    object data = (object)sampleDateTime;
        //    byte[] zipped = ZipZeroLength(data);
        //    object rawObject = UnZipZeroLength(zipped);

        //    DateTime returnedDateTime = (DateTime)rawObject;

        //    Assert.AreEqual(sampleDateTime, returnedDateTime);

        //    string sampleString = "Mary had a giant cat it ears were green and smelly";
        //    zipped = ZipZeroLength(sampleString);

        //    rawObject = UnZipZeroLength(zipped);

        //    string returnedString = rawObject as string;

        //    Assert.AreEqual(sampleString, returnedString);
        //}

        //byte[] ZipZeroLength(object data)
        //{
        //    BinaryFormatter formatter = new BinaryFormatter();
        //    MemoryStream memStream = new MemoryStream();

        //    using (ZipOutputStream zipStream = new ZipOutputStream(memStream)) {
        //        zipStream.PutNextEntry(new ZipEntry("data"));
        //        formatter.Serialize(zipStream, data);
        //        zipStream.CloseEntry();
        //        zipStream.Close();
        //    }

        //    byte[] result = memStream.ToArray();
        //    memStream.Close();

        //    return result;
        //}

        //object UnZipZeroLength(byte[] zipped)
        //{
        //    if (zipped == null)
        //    {
        //        return null;
        //    }

        //    object result = null;
        //    BinaryFormatter formatter = new BinaryFormatter();
        //    MemoryStream memStream = new MemoryStream(zipped);
        //    using (ZipInputStream zipStream = new ZipInputStream(memStream)) {
        //        ZipEntry zipEntry = zipStream.GetNextEntry();
        //        if (zipEntry != null) {
        //            result = formatter.Deserialize(zipStream);
        //        }
        //        zipStream.Close();
        //    }
        //    memStream.Close();

        //    return result;
        //}

        void CheckNameConversion(string toCheck)
        {
            byte[] intermediate = ZipConstants.ConvertToArray(toCheck);
            string final = ZipConstants.ConvertToString(intermediate);

            Assert.AreEqual(toCheck, final, "Expected identical result");
        }

        [TestMethod]
        [Tag("Zip")]
        public void NameConversion()
        {
            CheckNameConversion("Hello");
            CheckNameConversion("a/b/c/d/e/f/g/h/SomethingLikeAnArchiveName.txt");
        }

        [TestMethod]
        [Tag("Zip")]
        public void UnicodeNameConversion()
        {
            ZipConstants.DefaultCodePage = Encoding.UTF8.WebName;
            string sample = "Hello world";

            byte[] rawData = Encoding.UTF8.GetBytes(sample);

            string converted = ZipConstants.ConvertToStringExt(0, rawData);
            Assert.AreEqual(sample, converted);

            converted = ZipConstants.ConvertToStringExt((int)GeneralBitFlags.UnicodeText, rawData);
            Assert.AreEqual(sample, converted);

            // This time use some greek characters
            sample = "\u03A5\u03d5\u03a3";
            rawData = Encoding.UTF8.GetBytes(sample);

            converted = ZipConstants.ConvertToStringExt((int)GeneralBitFlags.UnicodeText, rawData);
            Assert.AreEqual(sample, converted);
        }

        /// <summary>
        /// Regression test for problem where the password check would fail for an archive whose
        /// date was updated from the extra data.
        /// This applies to archives where the crc wasnt know at the time of encryption.
        /// The date of the entry is used in its place.
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void PasswordCheckingWithDateInExtraData()
        {
            MemoryStream ms = new MemoryStream();
            DateTime checkTime = new DateTime(2010, 10, 16, 0, 3, 28);

            using (ZipOutputStream zos = new ZipOutputStream(ms))
            {
                zos.IsStreamOwner = false;
                zos.Password = "secret";
                ZipEntry ze = new ZipEntry("uno");
                ze.DateTime = new DateTime(1998, 6, 5, 4, 3, 2);

                ZipExtraData zed = new ZipExtraData();

                zed.StartNewEntry();

                zed.AddData(1);

                TimeSpan delta = checkTime.ToUniversalTime() - new System.DateTime(1970, 1, 1, 0, 0, 0).ToUniversalTime();
                int seconds = (int)delta.TotalSeconds;
                zed.AddLeInt(seconds);
                zed.AddNewEntry(0x5455);

                ze.ExtraData = zed.GetEntryData();
                zos.PutNextEntry(ze);
                zos.WriteByte(54);
            }

            ms.Position = 0;
            using (ZipInputStream zis = new ZipInputStream(ms))
            {
                zis.Password = "secret";
                ZipEntry uno = zis.GetNextEntry();
                byte theByte = (byte)zis.ReadByte();
                Assert.AreEqual(54, theByte);
                Assert.AreEqual(-1, zis.ReadByte());
                Assert.AreEqual(checkTime, uno.DateTime);
            }
        }
    }

    [TestClass]
    public class ZipExtraDataHandling : ZipBase
    {
        /// <summary>
        /// Extra data for separate entries should be unique to that entry
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void IsDataUnique()
        {
            ZipEntry a = new ZipEntry("Basil");
            byte[] extra = new byte[4];
            extra[0] = 27;
            a.ExtraData = extra;

            ZipEntry b = (ZipEntry)a.Clone();
            b.ExtraData[0] = 89;
            Assert.IsTrue(b.ExtraData[0] != a.ExtraData[0], "Extra data not unique " + b.ExtraData[0] + " " + a.ExtraData[0]);

            ZipEntry c = (ZipEntry)a.Clone();
            c.ExtraData[0] = 45;
            Assert.IsTrue(a.ExtraData[0] != c.ExtraData[0], "Extra data not unique " + a.ExtraData[0] + " " + c.ExtraData[0]);
        }

        [TestMethod]
        [Tag("Zip")]
        public void ExceedSize()
        {
            ZipExtraData zed = new ZipExtraData();
            byte[] buffer = new byte[65506];
            zed.AddEntry(1, buffer);
            Assert.AreEqual(65510, zed.Length);
            zed.AddEntry(2, new byte[21]);
            Assert.AreEqual(65535, zed.Length);

            bool caught = false;
            try
            {
                zed.AddEntry(3, null);
            }
            catch
            {
                caught = true;
            }

            Assert.IsTrue(caught, "Expected an exception when max size exceeded");
            Assert.AreEqual(65535, zed.Length);

            zed.Delete(2);
            Assert.AreEqual(65510, zed.Length);

            caught = false;
            try
            {
                zed.AddEntry(2, new byte[22]);
            }
            catch
            {
                caught = true;
            }
            Assert.IsTrue(caught, "Expected an exception when max size exceeded");
            Assert.AreEqual(65510, zed.Length);
        }

        [TestMethod]
        [Tag("Zip")]
        public void Deleting()
        {
            ZipExtraData zed = new ZipExtraData();
            Assert.AreEqual(0, zed.Length);

            // Tag 1 Totoal length 10
            zed.AddEntry(1, new byte[] { 10, 11, 12, 13, 14, 15 });
            Assert.AreEqual(10, zed.Length, "Length should be 10");
            Assert.AreEqual(10, zed.GetEntryData().Length, "Data length should be 10");

            // Tag 2 total length  9
            zed.AddEntry(2, new byte[] { 20, 21, 22, 23, 24 });
            Assert.AreEqual(19, zed.Length, "Length should be 19");
            Assert.AreEqual(19, zed.GetEntryData().Length, "Data length should be 19");

            // Tag 3 Total Length 6
            zed.AddEntry(3, new byte[] { 30, 31 });
            Assert.AreEqual(25, zed.Length, "Length should be 25");
            Assert.AreEqual(25, zed.GetEntryData().Length, "Data length should be 25");

            zed.Delete(2);
            Assert.AreEqual(16, zed.Length, "Length should be 16");
            Assert.AreEqual(16, zed.GetEntryData().Length, "Data length should be 16");

            // Tag 2 total length  9
            zed.AddEntry(2, new byte[] { 20, 21, 22, 23, 24 });
            Assert.AreEqual(25, zed.Length, "Length should be 25");
            Assert.AreEqual(25, zed.GetEntryData().Length, "Data length should be 25");

            zed.AddEntry(3, null);
            Assert.AreEqual(23, zed.Length, "Length should be 23");
            Assert.AreEqual(23, zed.GetEntryData().Length, "Data length should be 23");
        }

        [TestMethod]
        [Tag("Zip")]
        public void BasicOperations()
        {
            ZipExtraData zed = new ZipExtraData(null);
            Assert.AreEqual(0, zed.Length);

            zed = new ZipExtraData(new byte[] { 1, 0, 0, 0 });
            Assert.AreEqual(4, zed.Length, "A length should be 4");

            ZipExtraData zed2 = new ZipExtraData();
            Assert.AreEqual(0, zed2.Length);

            zed2.AddEntry(1, new byte[] { });

            byte[] data = zed.GetEntryData();
            for (int i = 0; i < data.Length; ++i)
            {
                Assert.AreEqual(zed2.GetEntryData()[i], data[i]);
            }

            Assert.AreEqual(4, zed2.Length, "A1 length should be 4");

            bool findResult = zed.Find(2);
            Assert.IsFalse(findResult, "A - Shouldnt find tag 2");

            findResult = zed.Find(1);
            Assert.IsTrue(findResult, "A - Should find tag 1");
            Assert.AreEqual(0, zed.ValueLength, "A- Length of entry should be 0");
            Assert.AreEqual(-1, zed.ReadByte());
            Assert.AreEqual(0, zed.GetStreamForTag(1).Length, "A - Length of stream should be 0");

            zed = new ZipExtraData(new byte[] { 1, 0, 3, 0, 1, 2, 3 });
            Assert.AreEqual(7, zed.Length, "Expected a length of 7");

            findResult = zed.Find(1);
            Assert.IsTrue(findResult, "B - Should find tag 1");
            Assert.AreEqual(3, zed.ValueLength, "B - Length of entry should be 3");
            for (int i = 1; i <= 3; ++i)
            {
                Assert.AreEqual(i, zed.ReadByte());
            }
            Assert.AreEqual(-1, zed.ReadByte());

            Stream s = zed.GetStreamForTag(1);
            Assert.AreEqual(3, s.Length, "B.1 Stream length should be 3");
            for (int i = 1; i <= 3; ++i)
            {
                Assert.AreEqual(i, s.ReadByte());
            }
            Assert.AreEqual(-1, s.ReadByte());

            zed = new ZipExtraData(new byte[] { 1, 0, 3, 0, 1, 2, 3, 2, 0, 1, 0, 56 });
            Assert.AreEqual(12, zed.Length, "Expected a length of 12");

            findResult = zed.Find(1);
            Assert.IsTrue(findResult, "C.1 - Should find tag 1");
            Assert.AreEqual(3, zed.ValueLength, "C.1 - Length of entry should be 3");
            for (int i = 1; i <= 3; ++i)
            {
                Assert.AreEqual(i, zed.ReadByte());
            }
            Assert.AreEqual(-1, zed.ReadByte());

            findResult = zed.Find(2);
            Assert.IsTrue(findResult, "C.2 - Should find tag 2");
            Assert.AreEqual(1, zed.ValueLength, "C.2 - Length of entry should be 1");
            Assert.AreEqual(56, zed.ReadByte());
            Assert.AreEqual(-1, zed.ReadByte());

            s = zed.GetStreamForTag(2);
            Assert.AreEqual(1, s.Length);
            Assert.AreEqual(56, s.ReadByte());
            Assert.AreEqual(-1, s.ReadByte());

            zed = new ZipExtraData();
            zed.AddEntry(7, new byte[] { 33, 44, 55 });
            findResult = zed.Find(7);
            Assert.IsTrue(findResult, "Add.1 should find new tag");
            Assert.AreEqual(3, zed.ValueLength, "Add.1 length should be 3");
            Assert.AreEqual(33, zed.ReadByte());
            Assert.AreEqual(44, zed.ReadByte());
            Assert.AreEqual(55, zed.ReadByte());
            Assert.AreEqual(-1, zed.ReadByte());

            zed.AddEntry(7, null);
            findResult = zed.Find(7);
            Assert.IsTrue(findResult, "Add.2 should find new tag");
            Assert.AreEqual(0, zed.ValueLength, "Add.2 length should be 0");

            zed.StartNewEntry();
            zed.AddData(0xae);
            zed.AddNewEntry(55);

            findResult = zed.Find(55);
            Assert.IsTrue(findResult, "Add.3 should find new tag");
            Assert.AreEqual(1, zed.ValueLength, "Add.3 length should be 1");
            Assert.AreEqual(0xae, zed.ReadByte());
            Assert.AreEqual(-1, zed.ReadByte());

            zed = new ZipExtraData();
            zed.StartNewEntry();
            zed.AddLeLong(0);
            zed.AddLeLong(-4);
            zed.AddLeLong(-1);
            zed.AddLeLong(long.MaxValue);
            zed.AddLeLong(long.MinValue);
            zed.AddLeLong(0x123456789ABCDEF0);
            zed.AddLeLong(unchecked((long)0xFEDCBA9876543210));
            zed.AddNewEntry(567);

            s = zed.GetStreamForTag(567);
            long longValue = ReadLong(s);
            Assert.AreEqual(longValue, zed.ReadLong(), "Read/stream mismatch");
            Assert.AreEqual(0, longValue, "Expected long value of zero");

            longValue = ReadLong(s);
            Assert.AreEqual(longValue, zed.ReadLong(), "Read/stream mismatch");
            Assert.AreEqual(-4, longValue, "Expected long value of -4");

            longValue = ReadLong(s);
            Assert.AreEqual(longValue, zed.ReadLong(), "Read/stream mismatch");
            Assert.AreEqual(-1, longValue, "Expected long value of -1");

            longValue = ReadLong(s);
            Assert.AreEqual(longValue, zed.ReadLong(), "Read/stream mismatch");
            Assert.AreEqual(long.MaxValue, longValue, "Expected long value of MaxValue");

            longValue = ReadLong(s);
            Assert.AreEqual(longValue, zed.ReadLong(), "Read/stream mismatch");
            Assert.AreEqual(long.MinValue, longValue, "Expected long value of MinValue");

            longValue = ReadLong(s);
            Assert.AreEqual(longValue, zed.ReadLong(), "Read/stream mismatch");
            Assert.AreEqual(0x123456789abcdef0, longValue, "Expected long value of MinValue");

            longValue = ReadLong(s);
            Assert.AreEqual(longValue, zed.ReadLong(), "Read/stream mismatch");
            Assert.AreEqual(unchecked((long)0xFEDCBA9876543210), longValue, "Expected long value of MinValue");
        }

        [TestMethod]
        [Tag("Zip")]
        public void UnreadCountValid()
        {
            ZipExtraData zed = new ZipExtraData(new byte[] { 1, 0, 0, 0 });
            Assert.AreEqual(4, zed.Length, "Length should be 4");
            Assert.IsTrue(zed.Find(1), "Should find tag 1");
            Assert.AreEqual(0, zed.UnreadCount);

            // seven bytes
            zed = new ZipExtraData(new byte[] { 1, 0, 7, 0, 1, 2, 3, 4, 5, 6, 7 });
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            for (int i = 0; i < 7; ++i)
            {
                Assert.AreEqual(7 - i, zed.UnreadCount);
                zed.ReadByte();
            }

            zed.ReadByte();
            Assert.AreEqual(0, zed.UnreadCount);
        }

        [TestMethod]
        [Tag("Zip")]
        public void Skipping()
        {
            ZipExtraData zed = new ZipExtraData(new byte[] { 1, 0, 7, 0, 1, 2, 3, 4, 5, 6, 7 });
            Assert.AreEqual(11, zed.Length, "Length should be 11");
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            Assert.AreEqual(7, zed.UnreadCount);
            Assert.AreEqual(4, zed.CurrentReadIndex);

            zed.ReadByte();
            Assert.AreEqual(6, zed.UnreadCount);
            Assert.AreEqual(5, zed.CurrentReadIndex);

            zed.Skip(1);
            Assert.AreEqual(5, zed.UnreadCount);
            Assert.AreEqual(6, zed.CurrentReadIndex);

            zed.Skip(-1);
            Assert.AreEqual(6, zed.UnreadCount);
            Assert.AreEqual(5, zed.CurrentReadIndex);

            zed.Skip(6);
            Assert.AreEqual(0, zed.UnreadCount);
            Assert.AreEqual(11, zed.CurrentReadIndex);

            bool exceptionCaught = false;

            try
            {
                zed.Skip(1);
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Should fail to skip past end");

            Assert.AreEqual(0, zed.UnreadCount);
            Assert.AreEqual(11, zed.CurrentReadIndex);

            zed.Skip(-7);
            Assert.AreEqual(7, zed.UnreadCount);
            Assert.AreEqual(4, zed.CurrentReadIndex);

            exceptionCaught = false;
            try
            {
                zed.Skip(-1);
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Should fail to skip before beginning");
        }

        [TestMethod]
        [Tag("Zip")]
        public void ReadOverrunLong()
        {
            ZipExtraData zed = new ZipExtraData(new byte[] { 1, 0, 0, 0 });
            Assert.AreEqual(4, zed.Length, "Length should be 4");
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            // Empty Tag
            bool exceptionCaught = false;
            try
            {
                zed.ReadLong();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");

            // seven bytes
            zed = new ZipExtraData(new byte[] { 1, 0, 7, 0, 1, 2, 3, 4, 5, 6, 7 });
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            exceptionCaught = false;
            try
            {
                zed.ReadLong();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");

            zed = new ZipExtraData(new byte[] { 1, 0, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 });
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            zed.ReadLong();

            exceptionCaught = false;
            try
            {
                zed.ReadLong();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");
        }

        [TestMethod]
        [Tag("Zip")]
        public void ReadOverrunInt()
        {
            ZipExtraData zed = new ZipExtraData(new byte[] { 1, 0, 0, 0 });
            Assert.AreEqual(4, zed.Length, "Length should be 4");
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            // Empty Tag
            bool exceptionCaught = false;
            try
            {
                zed.ReadInt();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");

            // three bytes
            zed = new ZipExtraData(new byte[] { 1, 0, 3, 0, 1, 2, 3 });
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            exceptionCaught = false;
            try
            {
                zed.ReadInt();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");

            zed = new ZipExtraData(new byte[] { 1, 0, 7, 0, 1, 2, 3, 4, 5, 6, 7 });
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            zed.ReadInt();

            exceptionCaught = false;
            try
            {
                zed.ReadInt();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");
        }

        [TestMethod]
        [Tag("Zip")]
        public void ReadOverrunShort()
        {
            ZipExtraData zed = new ZipExtraData(new byte[] { 1, 0, 0, 0 });
            Assert.AreEqual(4, zed.Length, "Length should be 4");
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            // Empty Tag
            bool exceptionCaught = false;
            try
            {
                zed.ReadShort();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");

            // Single byte
            zed = new ZipExtraData(new byte[] { 1, 0, 1, 0, 1 });
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            exceptionCaught = false;
            try
            {
                zed.ReadShort();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");

            zed = new ZipExtraData(new byte[] { 1, 0, 2, 0, 1, 2 });
            Assert.IsTrue(zed.Find(1), "Should find tag 1");

            zed.ReadShort();

            exceptionCaught = false;
            try
            {
                zed.ReadShort();
            }
            catch (ZipException)
            {
                exceptionCaught = true;
            }
            Assert.IsTrue(exceptionCaught, "Expected EOS exception");
        }

        [TestMethod]
        [Tag("Zip")]
        public void TaggedDataHandling()
        {
            NTTaggedData tagData = new NTTaggedData();
            DateTime modTime = tagData.LastModificationTime;
            byte[] rawData = tagData.GetData();
            tagData.LastModificationTime = tagData.LastModificationTime + TimeSpan.FromSeconds(40);
            Assert.AreNotEqual(tagData.LastModificationTime, modTime);
            tagData.SetData(rawData, 0, rawData.Length);
            Assert.AreEqual(10, tagData.TagID, "TagID mismatch");
            Assert.AreEqual(modTime, tagData.LastModificationTime, "NT Mod time incorrect");

            tagData.CreateTime = DateTime.FromFileTimeUtc(0);
            tagData.LastAccessTime = new DateTime(9999, 12, 31, 23, 59, 59);
            rawData = tagData.GetData();

            ExtendedUnixData unixData = new ExtendedUnixData();
            modTime = unixData.ModificationTime;
            unixData.ModificationTime = modTime; // Ensure flag is set.

            rawData = unixData.GetData();
            unixData.ModificationTime += TimeSpan.FromSeconds(100);
            Assert.AreNotEqual(unixData.ModificationTime, modTime);
            unixData.SetData(rawData, 0, rawData.Length);
            Assert.AreEqual(0x5455, unixData.TagID, "TagID mismatch");
            Assert.AreEqual(modTime, unixData.ModificationTime, "Unix mod time incorrect");
        }
    }



    [TestClass]
    public class ZipFileHandling : ZipBase
    {
        [TestMethod]
        [Tag("Zip")]
        public void NullStreamDetected()
        {
            ZipFile bad = null;
            FileStream nullStream = null;

            bool nullStreamDetected = false;

            try
            {
                bad = new ZipFile(nullStream);
            }
            catch
            {
                nullStreamDetected = true;
            }

            Assert.IsTrue(nullStreamDetected, "Null stream should be detected in ZipFile constructor");
            Assert.IsNull(bad, "ZipFile instance should not be created");
        }

        

        [TestMethod]
        [Tag("Zip")]
        public void EmbeddedArchive()
        {
            MemoryStream memStream = new MemoryStream();
            using (ZipFile f = new ZipFile(memStream))
            {
                f.IsStreamOwner = false;

                StringMemoryDataSource m = new StringMemoryDataSource("0000000");
                f.BeginUpdate(new MemoryArchiveStorage());
                f.Add(m, "a.dat");
                f.Add(m, "b.dat");
                f.CommitUpdate();
                Assert.IsTrue(f.TestArchive(true));
            }

            byte[] rawArchive = memStream.ToArray();
            byte[] pseudoSfx = new byte[1049 + rawArchive.Length];
            Array.Copy(rawArchive, 0, pseudoSfx, 1049, rawArchive.Length);

            memStream = new MemoryStream(pseudoSfx);
            using (ZipFile f = new ZipFile(memStream))
            {
                for (int index = 0; index < f.Count; ++index)
                {
                    Stream entryStream = f.GetInputStream(index);
                    MemoryStream data = new MemoryStream();
                    StreamUtils.Copy(entryStream, data, new byte[128]);
                    byte[] data3 = data.ToArray();
                    string contents = Encoding.UTF8.GetString(data3, 0, data3.Length);
                    Assert.AreEqual("0000000", contents);
                }
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void Zip64Useage()
        {
            MemoryStream memStream = new MemoryStream();
            using (ZipFile f = new ZipFile(memStream))
            {
                f.IsStreamOwner = false;
                f.UseZip64 = UseZip64.On;

                StringMemoryDataSource m = new StringMemoryDataSource("0000000");
                f.BeginUpdate(new MemoryArchiveStorage());
                f.Add(m, "a.dat");
                f.Add(m, "b.dat");
                f.CommitUpdate();
                Assert.IsTrue(f.TestArchive(true));
            }

            byte[] rawArchive = memStream.ToArray();

            byte[] pseudoSfx = new byte[1049 + rawArchive.Length];
            Array.Copy(rawArchive, 0, pseudoSfx, 1049, rawArchive.Length);

            memStream = new MemoryStream(pseudoSfx);
            using (ZipFile f = new ZipFile(memStream))
            {
                for (int index = 0; index < f.Count; ++index)
                {
                    Stream entryStream = f.GetInputStream(index);
                    MemoryStream data = new MemoryStream();
                    StreamUtils.Copy(entryStream, data, new byte[128]);
                    byte[] data2 = data.ToArray();
                    string contents = Encoding.UTF8.GetString(data2, 0, data2.Length);
                    Assert.AreEqual("0000000", contents);
                }
            }
        }

        //[TestMethod]
        //[Tag("Zip")]
        ////[Explicit]
        //public void Zip64Offset()
        //{
        //    // TODO: Test to check that a zip64 offset value is loaded correctly.
        //    // Changes in ZipEntry to CentralHeaderRequiresZip64 and LocalHeaderRequiresZip64
        //    // were not quite correct...
        //}

        [TestMethod]
        [Tag("Zip")]
        public void BasicEncryption()
        {
            const string TestValue = "0001000";
            MemoryStream memStream = new MemoryStream();
            using (ZipFile f = new ZipFile(memStream))
            {
                f.IsStreamOwner = false;
                f.Password = "Hello";

                StringMemoryDataSource m = new StringMemoryDataSource(TestValue);
                f.BeginUpdate(new MemoryArchiveStorage());
                f.Add(m, "a.dat");
                f.CommitUpdate();
                Assert.IsTrue(f.TestArchive(true), "Archive test should pass");
            }

            using (ZipFile g = new ZipFile(memStream))
            {
                g.Password = "Hello";
                ZipEntry ze = g[0];

                Assert.IsTrue(ze.IsCrypted, "Entry should be encrypted");
                using (StreamReader r = new StreamReader(g.GetInputStream(0)))
                {
                    string data = r.ReadToEnd();
                    Assert.AreEqual(TestValue, data);
                }
            }
        }

        
        [TestMethod]
        [Tag("Zip")]
        public void AddEncryptedEntriesToExistingArchive()
        {
            const string TestValue = "0001000";
            MemoryStream memStream = new MemoryStream();
            using (ZipFile f = new ZipFile(memStream))
            {
                f.IsStreamOwner = false;
                f.UseZip64 = UseZip64.Off;

                StringMemoryDataSource m = new StringMemoryDataSource(TestValue);
                f.BeginUpdate(new MemoryArchiveStorage());
                f.Add(m, "a.dat");
                f.CommitUpdate();
                Assert.IsTrue(f.TestArchive(true), "Archive test should pass");
            }

            using (ZipFile g = new ZipFile(memStream))
            {
                ZipEntry ze = g[0];

                Assert.IsFalse(ze.IsCrypted, "Entry should NOT be encrypted");
                using (StreamReader r = new StreamReader(g.GetInputStream(0)))
                {
                    string data = r.ReadToEnd();
                    Assert.AreEqual(TestValue, data);
                }

                StringMemoryDataSource n = new StringMemoryDataSource(TestValue);

                g.Password = "Axolotyl";
                g.UseZip64 = UseZip64.Off;
                g.IsStreamOwner = false;
                g.BeginUpdate();
                g.Add(n, "a1.dat");
                g.CommitUpdate();
                Assert.IsTrue(g.TestArchive(true), "Archive test should pass");
                ze = g[1];
                Assert.IsTrue(ze.IsCrypted, "New entry should be encrypted");

                using (StreamReader r = new StreamReader(g.GetInputStream(0)))
                {
                    string data = r.ReadToEnd();
                    Assert.AreEqual(TestValue, data);
                }
            }
        }

        void TryDeleting(byte[] master, int totalEntries, int additions, params string[] toDelete)
        {
            MemoryStream ms = new MemoryStream();
            ms.Write(master, 0, master.Length);

            using (ZipFile f = new ZipFile(ms))
            {
                f.IsStreamOwner = false;
                Assert.AreEqual(totalEntries, f.Count);
                Assert.IsTrue(f.TestArchive(true));
                f.BeginUpdate(new MemoryArchiveStorage());

                for (int i = 0; i < additions; ++i)
                {
                    f.Add(new StringMemoryDataSource("Another great file"),
                        string.Format("Add{0}.dat", i + 1));
                }

                foreach (string name in toDelete)
                {
                    f.Delete(name);
                }
                f.CommitUpdate();

                // write stream to file to assist debugging.
                // WriteToFile(@"c:\aha.zip", ms.ToArray());

                int newTotal = totalEntries + additions - toDelete.Length;
                Assert.AreEqual(newTotal, f.Count,
                    string.Format("Expected {0} entries after update found {1}", newTotal, f.Count));
                Assert.IsTrue(f.TestArchive(true), "Archive test should pass");
            }
        }

        void TryDeleting(byte[] master, int totalEntries, int additions, params int[] toDelete)
        {
            MemoryStream ms = new MemoryStream();
            ms.Write(master, 0, master.Length);

            using (ZipFile f = new ZipFile(ms))
            {
                f.IsStreamOwner = false;
                Assert.AreEqual(totalEntries, f.Count);
                Assert.IsTrue(f.TestArchive(true));
                f.BeginUpdate(new MemoryArchiveStorage());

                for (int i = 0; i < additions; ++i)
                {
                    f.Add(new StringMemoryDataSource("Another great file"),
                        string.Format("Add{0}.dat", i + 1));
                }

                foreach (int i in toDelete)
                {
                    f.Delete(f[i]);
                }
                f.CommitUpdate();

                /* write stream to file to assist debugging.
                                byte[] data = ms.ToArray();
                                using ( FileStream fs = store.OpenFile(@"c:\aha.zip", FileMode.Create, FileAccess.ReadWrite, FileShare.Read) ) {
                                    fs.Write(data, 0, data.Length);
                                }
                */
                int newTotal = totalEntries + additions - toDelete.Length;
                Assert.AreEqual(newTotal, f.Count,
                    string.Format("Expected {0} entries after update found {1}", newTotal, f.Count));
                Assert.IsTrue(f.TestArchive(true), "Archive test should pass");
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void AddAndDeleteEntriesMemory()
        {
            MemoryStream memStream = new MemoryStream();

            using (ZipFile f = new ZipFile(memStream))
            {
                f.IsStreamOwner = false;

                f.BeginUpdate(new MemoryArchiveStorage());
                f.Add(new StringMemoryDataSource("Hello world"), @"z:\a\a.dat");
                f.Add(new StringMemoryDataSource("Another"), @"\b\b.dat");
                f.Add(new StringMemoryDataSource("Mr C"), @"c\c.dat");
                f.Add(new StringMemoryDataSource("Mrs D was a star"), @"d\d.dat");
                f.CommitUpdate();
                Assert.IsTrue(f.TestArchive(true));
            }

            byte[] master = memStream.ToArray();

            TryDeleting(master, 4, 1, @"z:\a\a.dat");
            TryDeleting(master, 4, 1, @"\a\a.dat");
            TryDeleting(master, 4, 1, @"a/a.dat");

            TryDeleting(master, 4, 0, 0);
            TryDeleting(master, 4, 0, 1);
            TryDeleting(master, 4, 0, 2);
            TryDeleting(master, 4, 0, 3); // failing here in 4 and 3
            TryDeleting(master, 4, 0, 0, 1);
            TryDeleting(master, 4, 0, 0, 2);
            TryDeleting(master, 4, 0, 0, 3);
            TryDeleting(master, 4, 0, 1, 2);
            TryDeleting(master, 4, 0, 1, 3);
            TryDeleting(master, 4, 0, 2);

            TryDeleting(master, 4, 1, 0);
            TryDeleting(master, 4, 1, 1);
            TryDeleting(master, 4, 3, 2);
            TryDeleting(master, 4, 4, 3);
            TryDeleting(master, 4, 10, 0, 1);
            TryDeleting(master, 4, 10, 0, 2);
            TryDeleting(master, 4, 10, 0, 3);
            TryDeleting(master, 4, 20, 1, 2);
            TryDeleting(master, 4, 30, 1, 3);
            TryDeleting(master, 4, 40, 2);
        }




        /// <summary>
        /// Simple round trip test for ZipFile class
        /// </summary>
        [TestMethod]
        [Tag("Zip")]
        public void RoundTripInMemory()
        {
            MemoryStream storage = new MemoryStream();
            MakeZipFile(storage, false, "", 10, 1024, "");

            using (ZipFile zipFile = new ZipFile(storage))
            {
                foreach (ZipEntry e in zipFile)
                {
                    Stream instream = zipFile.GetInputStream(e);
                    CheckKnownEntry(instream, 1024);
                }
                zipFile.Close();
            }
        }







        [TestMethod]
        [Tag("Zip")]
        public void ArchiveTesting()
        {
            byte[] originalData = null;
            byte[] compressedData = MakeInMemoryZip(ref originalData, CompressionMethod.Deflated,
                6, 1024, null, true);

            MemoryStream ms = new MemoryStream(compressedData);
            ms.Seek(0, SeekOrigin.Begin);

            using (ZipFile testFile = new ZipFile(ms))
            {

                Assert.IsTrue(testFile.TestArchive(true), "Unexpected error in archive detected");

                byte[] corrupted = new byte[compressedData.Length];
                Array.Copy(compressedData, corrupted, compressedData.Length);

                corrupted[123] = (byte)(~corrupted[123] & 0xff);
                ms = new MemoryStream(corrupted);
            }

            using (ZipFile testFile = new ZipFile(ms))
            {
                Assert.IsFalse(testFile.TestArchive(true), "Error in archive not detected");
            }
        }

        void TestDirectoryEntry(MemoryStream s)
        {
            ZipOutputStream outStream = new ZipOutputStream(s);
            outStream.IsStreamOwner = false;
            outStream.PutNextEntry(new ZipEntry("YeOldeDirectory/"));
            outStream.Close();

            MemoryStream ms2 = new MemoryStream(s.ToArray());
            using (ZipFile zf = new ZipFile(ms2))
            {
                Assert.IsTrue(zf.TestArchive(true));
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void TestDirectoryEntry()
        {
            TestDirectoryEntry(new MemoryStream());
            TestDirectoryEntry(new MemoryStreamWithoutSeek());
        }

        void TestEncryptedDirectoryEntry(MemoryStream s)
        {
            ZipOutputStream outStream = new ZipOutputStream(s);
            outStream.Password = "Tonto hand me a beer";

            outStream.IsStreamOwner = false;
            outStream.PutNextEntry(new ZipEntry("YeUnreadableDirectory/"));
            outStream.Close();

            MemoryStream ms2 = new MemoryStream(s.ToArray());
            using (ZipFile zf = new ZipFile(ms2))
            {
                Assert.IsTrue(zf.TestArchive(true));
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void TestEncryptedDirectoryEntry()
        {
            TestEncryptedDirectoryEntry(new MemoryStream());
            TestEncryptedDirectoryEntry(new MemoryStreamWithoutSeek());
        }

        [TestMethod]
        [Tag("Zip")]
        public void Crypto_AddEncryptedEntryToExistingArchiveSafe()
        {
            MemoryStream ms = new MemoryStream();

            byte[] rawData;

            using (ZipFile testFile = new ZipFile(ms))
            {
                testFile.IsStreamOwner = false;
                testFile.BeginUpdate();
                testFile.Add(new StringMemoryDataSource("Aha"), "No1", CompressionMethod.Stored);
                testFile.Add(new StringMemoryDataSource("And so it goes"), "No2", CompressionMethod.Stored);
                testFile.Add(new StringMemoryDataSource("No3"), "No3", CompressionMethod.Stored);
                testFile.CommitUpdate();

                Assert.IsTrue(testFile.TestArchive(true));
                rawData = ms.ToArray();
            }

            ms = new MemoryStream(rawData);

            using (ZipFile testFile = new ZipFile(ms))
            {
                Assert.IsTrue(testFile.TestArchive(true));

                testFile.BeginUpdate(new MemoryArchiveStorage(FileUpdateMode.Safe));
                testFile.Password = "pwd";
                testFile.Add(new StringMemoryDataSource("Zapata!"), "encrypttest.xml");
                testFile.CommitUpdate();

                Assert.IsTrue(testFile.TestArchive(true));

                int entryIndex = testFile.FindEntry("encrypttest.xml", true);
                Assert.IsNotNull(entryIndex >= 0);
                Assert.IsTrue(testFile[entryIndex].IsCrypted);
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void Crypto_AddEncryptedEntryToExistingArchiveDirect()
        {
            MemoryStream ms = new MemoryStream();

            using (ZipFile testFile = new ZipFile(ms))
            {
                testFile.IsStreamOwner = false;
                testFile.BeginUpdate();
                testFile.Add(new StringMemoryDataSource("Aha"), "No1", CompressionMethod.Stored);
                testFile.Add(new StringMemoryDataSource("And so it goes"), "No2", CompressionMethod.Stored);
                testFile.Add(new StringMemoryDataSource("No3"), "No3", CompressionMethod.Stored);
                testFile.CommitUpdate();

                Assert.IsTrue(testFile.TestArchive(true));
            }

            using (ZipFile testFile = new ZipFile(ms))
            {
                Assert.IsTrue(testFile.TestArchive(true));
                testFile.IsStreamOwner = true;

                testFile.BeginUpdate();
                testFile.Password = "pwd";
                testFile.Add(new StringMemoryDataSource("Zapata!"), "encrypttest.xml");
                testFile.CommitUpdate();

                Assert.IsTrue(testFile.TestArchive(true));

                int entryIndex = testFile.FindEntry("encrypttest.xml", true);
                Assert.IsNotNull(entryIndex >= 0);
                Assert.IsTrue(testFile[entryIndex].IsCrypted);
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void UnicodeNames()
        {
            MemoryStream memStream = new MemoryStream();
            using (ZipFile f = new ZipFile(memStream))
            {
                f.IsStreamOwner = false;

                f.BeginUpdate(new MemoryArchiveStorage());

                string[] names = new string[] 
				{
					"\u030A\u03B0",     // Greek
					"\u0680\u0685",     // Arabic
				};

                foreach (string name in names)
                {
                    f.Add(new StringMemoryDataSource("Hello world"), name,
                          CompressionMethod.Deflated, true);
                }
                f.CommitUpdate();
                Assert.IsTrue(f.TestArchive(true));

                foreach (string name in names)
                {
                    int index = f.FindEntry(name, true);

                    Assert.IsTrue(index >= 0);
                    ZipEntry found = f[index];
                    Assert.AreEqual(name, found.Name);
                }
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void UpdateCommentOnlyInMemory()
        {
            MemoryStream ms = new MemoryStream();

            using (ZipFile testFile = new ZipFile(ms))
            {
                testFile.IsStreamOwner = false;
                testFile.BeginUpdate();
                testFile.Add(new StringMemoryDataSource("Aha"), "No1", CompressionMethod.Stored);
                testFile.Add(new StringMemoryDataSource("And so it goes"), "No2", CompressionMethod.Stored);
                testFile.Add(new StringMemoryDataSource("No3"), "No3", CompressionMethod.Stored);
                testFile.CommitUpdate();

                Assert.IsTrue(testFile.TestArchive(true));
            }

            using (ZipFile testFile = new ZipFile(ms))
            {
                Assert.IsTrue(testFile.TestArchive(true));
                Assert.AreEqual("", testFile.ZipFileComment);
                testFile.IsStreamOwner = false;

                testFile.BeginUpdate();
                testFile.SetComment("Here is my comment");
                testFile.CommitUpdate();

                Assert.IsTrue(testFile.TestArchive(true));
            }

            using (ZipFile testFile = new ZipFile(ms))
            {
                Assert.IsTrue(testFile.TestArchive(true));
                Assert.AreEqual("Here is my comment", testFile.ZipFileComment);
            }
        }

  

        [TestMethod]
        [Tag("Zip")]
        public void NameFactory()
        {
            MemoryStream memStream = new MemoryStream();
            DateTime fixedTime = new DateTime(1981, 4, 3);
            using (ZipFile f = new ZipFile(memStream))
            {
                f.IsStreamOwner = false;
                ((ZipEntryFactory)f.EntryFactory).IsUnicodeText = true;
                ((ZipEntryFactory)f.EntryFactory).Setting = ZipEntryFactory.TimeSetting.Fixed;
                ((ZipEntryFactory)f.EntryFactory).FixedDateTime = fixedTime;
                ((ZipEntryFactory)f.EntryFactory).SetAttributes = 1;
                f.BeginUpdate(new MemoryArchiveStorage());

                string[] names = new string[] 
				{
					"\u030A\u03B0",     // Greek
					"\u0680\u0685",     // Arabic
				};

                foreach (string name in names)
                {
                    f.Add(new StringMemoryDataSource("Hello world"), name,
                          CompressionMethod.Deflated, true);
                }
                f.CommitUpdate();
                Assert.IsTrue(f.TestArchive(true));

                foreach (string name in names)
                {
                    int index = f.FindEntry(name, true);

                    Assert.IsTrue(index >= 0);
                    ZipEntry found = f[index];
                    Assert.AreEqual(name, found.Name);
                    Assert.IsTrue(found.IsUnicodeText);
                    Assert.AreEqual(fixedTime, found.DateTime);
                    Assert.IsTrue(found.IsDOSEntry);
                }
            }
        }

        [TestMethod]
        [Tag("Zip")]
        public void NestedArchive()
        {
            MemoryStream ms = new MemoryStream();
            using (ZipOutputStream zos = new ZipOutputStream(ms))
            {
                zos.IsStreamOwner = false;
                ZipEntry ze = new ZipEntry("Nest1");

                zos.PutNextEntry(ze);
                byte[] toWrite = Encoding.UTF8.GetBytes("Hello");
                zos.Write(toWrite, 0, toWrite.Length);
            }

            byte[] data = ms.ToArray();

            ms = new MemoryStream();
            using (ZipOutputStream zos = new ZipOutputStream(ms))
            {
                zos.IsStreamOwner = false;
                ZipEntry ze = new ZipEntry("Container");
                ze.CompressionMethod = CompressionMethod.Stored;
                zos.PutNextEntry(ze);
                zos.Write(data, 0, data.Length);
            }

            using (ZipFile zipFile = new ZipFile(ms))
            {
                ZipEntry e = zipFile[0];
                Assert.AreEqual("Container", e.Name);

                using (ZipFile nested = new ZipFile(zipFile.GetInputStream(0)))
                {
                    Assert.IsTrue(nested.TestArchive(true));
                    Assert.AreEqual(1, nested.Count);

                    Stream nestedStream = nested.GetInputStream(0);

                    StreamReader reader = new StreamReader(nestedStream);

                    string contents = reader.ReadToEnd();

                    Assert.AreEqual("Hello", contents);
                }
            }
        }

        Stream GetPartialStream()
        {
            MemoryStream ms = new MemoryStream();
            using (ZipOutputStream zos = new ZipOutputStream(ms))
            {
                zos.IsStreamOwner = false;
                ZipEntry ze = new ZipEntry("E1");

                zos.PutNextEntry(ze);
                byte[] toWrite = Encoding.UTF8.GetBytes("Hello");
                zos.Write(toWrite, 0, toWrite.Length);
            }

            ZipFile zf = new ZipFile(ms);

            return zf.GetInputStream(0);
        }

        [TestMethod]
        public void UnreferencedZipFileClosingPartialStream()
        {
            Stream s = GetPartialStream();

            GC.Collect();

            s.ReadByte();
        }
    }

    [TestClass]
    public class ZipEntryFactoryHandling : ZipBase
    {
        // TODO: Complete testing for ZipEntryFactory

        // FileEntry creation and retrieval of information
        // DirectoryEntry creation and retrieval of information.

        [TestMethod]
        [Tag("Zip")]
        public void Defaults()
        {
            DateTime testStart = DateTime.Now;
            ZipEntryFactory f = new ZipEntryFactory();
            Assert.IsNotNull(f.NameTransform);
            Assert.AreEqual(-1, f.GetAttributes);
            Assert.AreEqual(0, f.SetAttributes);
            Assert.AreEqual(ZipEntryFactory.TimeSetting.LastWriteTime, f.Setting);

            //Assert.LessOrEqual(testStart, f.FixedDateTime);
            //Assert.GreaterOrEqual(DateTime.Now, f.FixedDateTime);
            Assert.IsTrue(testStart<= f.FixedDateTime);
            Assert.IsTrue(DateTime.Now>= f.FixedDateTime);

            f = new ZipEntryFactory(ZipEntryFactory.TimeSetting.LastAccessTimeUtc);
            Assert.IsNotNull(f.NameTransform);
            Assert.AreEqual(-1, f.GetAttributes);
            Assert.AreEqual(0, f.SetAttributes);
            Assert.AreEqual(ZipEntryFactory.TimeSetting.LastAccessTimeUtc, f.Setting);
            //Assert.LessOrEqual(testStart, f.FixedDateTime);
            //Assert.GreaterOrEqual(DateTime.Now, f.FixedDateTime);
            Assert.IsTrue(testStart<= f.FixedDateTime);
            Assert.IsTrue(DateTime.Now>= f.FixedDateTime);

            DateTime fixedDate = new DateTime(1999, 1, 2);
            f = new ZipEntryFactory(fixedDate);
            Assert.IsNotNull(f.NameTransform);
            Assert.AreEqual(-1, f.GetAttributes);
            Assert.AreEqual(0, f.SetAttributes);
            Assert.AreEqual(ZipEntryFactory.TimeSetting.Fixed, f.Setting);
            Assert.AreEqual(fixedDate, f.FixedDateTime);
        }

        [TestMethod]
        [Tag("Zip")]
        public void CreateInMemoryValues()
        {
            string tempFile = "bingo:";

            // Note the seconds returned will be even!
            DateTime epochTime = new DateTime(1980, 1, 1);
            DateTime createTime = new DateTime(2100, 2, 27, 11, 07, 56);
            DateTime lastWriteTime = new DateTime(2050, 11, 3, 7, 23, 32);
            DateTime lastAccessTime = new DateTime(2050, 11, 3, 0, 42, 12);

            ZipEntryFactory factory = new ZipEntryFactory();
            ZipEntry entry;
            int combinedAttributes;

            DateTime startTime = DateTime.Now;

            factory.Setting = ZipEntryFactory.TimeSetting.CreateTime;
            factory.GetAttributes = ~((int)FileAttributes.ReadOnly);
            factory.SetAttributes = (int)FileAttributes.ReadOnly;
            combinedAttributes = (int)FileAttributes.ReadOnly;

            entry = factory.MakeFileEntry(tempFile, false);
            Assert.IsTrue(TestHelper.CompareDosDateTimes(startTime, entry.DateTime) <= 0, "Create time failure");
            Assert.AreEqual(entry.ExternalFileAttributes, combinedAttributes);
            Assert.AreEqual(-1, entry.Size);

            factory.FixedDateTime = startTime;
            factory.Setting = ZipEntryFactory.TimeSetting.Fixed;
            entry = factory.MakeFileEntry(tempFile, false);
            Assert.AreEqual(0, TestHelper.CompareDosDateTimes(startTime, entry.DateTime), "Access time failure");
            Assert.AreEqual(-1, entry.Size);

            factory.Setting = ZipEntryFactory.TimeSetting.LastWriteTime;
            entry = factory.MakeFileEntry(tempFile, false);
            Assert.IsTrue(TestHelper.CompareDosDateTimes(startTime, entry.DateTime) <= 0, "Write time failure");
            Assert.AreEqual(-1, entry.Size);
        }


        ///// <summary>
        ///// This test is invalid for Silverlight/WP7 as silverlight does not allow for
        ///// setting of file dates
        ///// </summary>
        //[TestMethod]
        //[Tag("Zip")]
        //[Tag("CreatesTempFile")]
        //public void CreatedValues()
        //{
        //    string tempDir=GetTempFilePath();
        //    Assert.IsNotNull(tempDir, "No permission to execute this test?");

        //    tempDir=Path.Combine(tempDir, "SharpZipTest");

        //    if( tempDir!=null ) {

        //        Directory.CreateDirectory(tempDir);

        //        try {
        //            // Note the seconds returned will be even!
        //            DateTime createTime=new DateTime(2100, 2, 27, 11, 07, 56);
        //            DateTime lastWriteTime=new DateTime(2050, 11, 3, 7, 23, 32);
        //            DateTime lastAccessTime=new DateTime(2050, 11, 3, 0, 42, 12);

        //            string tempFile=Path.Combine(tempDir, "SharpZipTest.Zip");
        //            using( FileStream f=File.Create(tempFile, 1024) ) {
        //                f.WriteByte(0);
        //            }


        //            // Silverlight does not allow setting of file dates

        //            //File.SetCreationTime(tempFile, createTime);
        //            //File.SetLastWriteTime(tempFile, lastWriteTime);
        //            //File.SetLastAccessTime(tempFile, lastAccessTime);

        //            FileAttributes attributes=FileAttributes.Hidden;

        //            store.SetAttributes(tempFile, attributes);
        //            ZipEntryFactory factory=null;
        //            ZipEntry entry;
        //            int combinedAttributes=0;

        //            try {
        //                factory=new ZipEntryFactory();

        //                factory.Setting=ZipEntryFactory.TimeSetting.CreateTime;
        //                factory.GetAttributes=~((int)FileAttributes.ReadOnly);
        //                factory.SetAttributes=(int)FileAttributes.ReadOnly;
        //                combinedAttributes=(int)(FileAttributes.ReadOnly|FileAttributes.Hidden);

        //                entry=factory.MakeFileEntry(tempFile);
        //                Assert.AreEqual(createTime, entry.DateTime, "Create time failure"); // failing here in 4 and 3
        //                Assert.AreEqual(entry.ExternalFileAttributes, combinedAttributes);
        //                Assert.AreEqual(1, entry.Size);

        //                factory.Setting=ZipEntryFactory.TimeSetting.LastAccessTime;
        //                entry=factory.MakeFileEntry(tempFile);
        //                Assert.AreEqual(lastAccessTime, entry.DateTime, "Access time failure");
        //                Assert.AreEqual(1, entry.Size);

        //                factory.Setting=ZipEntryFactory.TimeSetting.LastWriteTime;
        //                entry=factory.MakeFileEntry(tempFile);
        //                Assert.AreEqual(lastWriteTime, entry.DateTime, "Write time failure");
        //                Assert.AreEqual(1, entry.Size);
        //            }
        //            finally {
        //                store.Delete(tempFile);
        //            }

        //            // Do the same for directories
        //            // Note the seconds returned will be even!
        //            createTime=new DateTime(2090, 2, 27, 11, 7, 56);
        //            lastWriteTime=new DateTime(2107, 12, 31, 23, 59, 58);
        //            lastAccessTime=new DateTime(1980, 1, 1, 1, 0, 0);

        //            //Directory.SetCreationTime(tempDir, createTime);
        //            //Directory.SetLastWriteTime(tempDir, lastWriteTime);
        //            //Directory.SetLastAccessTime(tempDir, lastAccessTime);

        //            factory.Setting=ZipEntryFactory.TimeSetting.CreateTime;
        //            entry=factory.MakeDirectoryEntry(tempDir);
        //            Assert.AreEqual(createTime, entry.DateTime, "Directory create time failure");
        //            Assert.IsTrue((entry.ExternalFileAttributes&(int)FileAttributes.Directory)==(int)FileAttributes.Directory);

        //            factory.Setting=ZipEntryFactory.TimeSetting.LastAccessTime;
        //            entry=factory.MakeDirectoryEntry(tempDir);
        //            Assert.AreEqual(lastAccessTime, entry.DateTime, "Directory access time failure");

        //            factory.Setting=ZipEntryFactory.TimeSetting.LastWriteTime;
        //            entry=factory.MakeDirectoryEntry(tempDir);
        //            Assert.AreEqual(lastWriteTime, entry.DateTime, "Directory write time failure");
        //        }
        //        finally {
        //            Directory.Delete(tempDir, true);
        //        }
        //    }
        //}
    }
}
