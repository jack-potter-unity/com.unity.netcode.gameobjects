﻿using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Multiplayer.Netcode;
using UnityEngine;
using Random = System.Random;

namespace Unity.Netcode.EditorTests
{
    public class BytePackerTests
    {
        #region Test Types

        private enum ByteEnum : byte
        {
            A,
            B,
            C
        }

        private enum SByteEnum : sbyte
        {
            A,
            B,
            C
        }

        private enum ShortEnum : short
        {
            A,
            B,
            C
        }

        private enum UShortEnum : ushort
        {
            A,
            B,
            C
        }

        private enum IntEnum
        {
            A,
            B,
            C
        }

        private enum UIntEnum : uint
        {
            A,
            B,
            C
        }

        private enum LongEnum : long
        {
            A,
            B,
            C
        }

        private enum ULongEnum : ulong
        {
            A,
            B,
            C
        }

        private struct TestStruct
        {
            public byte a;
            public short b;
            public ushort c;
            public int d;
            public uint e;
            public long f;
            public ulong g;
            public bool h;
            public char i;
            public float j;
            public double k;
        }

        public enum WriteType
        {
            WriteDirect,
            WriteAsObject
        }

        #endregion

        private void CheckUnsignedPackedSize64(ref FastBufferWriter writer, ulong value)
        {

            if (value <= 240)
            {
                Assert.AreEqual(1, writer.Position);
            }
            else if (value <= 2287)
            {
                Assert.AreEqual(2, writer.Position);
            }
            else
            {
                Assert.AreEqual(BitCounter.GetUsedByteCount(value) + 1, writer.Position);
            }
        }

        private void CheckUnsignedPackedValue64(ref FastBufferWriter writer, ulong value)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValuePacked(ref reader, out ulong readValue);
                Assert.AreEqual(readValue, value);
            }
        }

        private void CheckUnsignedPackedSize32(ref FastBufferWriter writer, uint value)
        {

            if (value <= 240)
            {
                Assert.AreEqual(1, writer.Position);
            }
            else if (value <= 2287)
            {
                Assert.AreEqual(2, writer.Position);
            }
            else
            {
                Assert.AreEqual(BitCounter.GetUsedByteCount(value) + 1, writer.Position);
            }
        }

        private void CheckUnsignedPackedValue32(ref FastBufferWriter writer, uint value)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValuePacked(ref reader, out uint readValue);
                Assert.AreEqual(readValue, value);
            }
        }

        private void CheckSignedPackedSize64(ref FastBufferWriter writer, long value)
        {
            ulong asUlong = Arithmetic.ZigZagEncode(value);

            if (asUlong <= 240)
            {
                Assert.AreEqual(1, writer.Position);
            }
            else if (asUlong <= 2287)
            {
                Assert.AreEqual(2, writer.Position);
            }
            else
            {
                Assert.AreEqual(BitCounter.GetUsedByteCount(asUlong) + 1, writer.Position);
            }
        }

        private void CheckSignedPackedValue64(ref FastBufferWriter writer, long value)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValuePacked(ref reader, out long readValue);
                Assert.AreEqual(readValue, value);
            }
        }

        private void CheckSignedPackedSize32(ref FastBufferWriter writer, int value)
        {
            ulong asUlong = Arithmetic.ZigZagEncode(value);

            if (asUlong <= 240)
            {
                Assert.AreEqual(1, writer.Position);
            }
            else if (asUlong <= 2287)
            {
                Assert.AreEqual(2, writer.Position);
            }
            else
            {
                Assert.AreEqual(BitCounter.GetUsedByteCount(asUlong) + 1, writer.Position);
            }
        }

        private void CheckSignedPackedValue32(ref FastBufferWriter writer, int value)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValuePacked(ref reader, out int readValue);
                Assert.AreEqual(readValue, value);
            }
        }
        
        private unsafe void VerifyBytewiseEquality<T>(T value, T otherValue) where T: unmanaged
        {
            byte* asBytePointer = (byte*) &value;
            byte* otherBytePointer = (byte*) &otherValue;
            for (var i = 0; i < sizeof(T); ++i)
            {
                Assert.AreEqual(asBytePointer[i], otherBytePointer[i]);
            }
        }

        private unsafe void RunTypeTest<T>(T value) where T : unmanaged
        {
            FastBufferWriter writer = new FastBufferWriter(sizeof(T)*2, Allocator.Temp);
            using (writer)
            {
                BytePacker.WriteValuePacked(ref writer, (dynamic)value);
                var reader = new FastBufferReader(ref writer, Allocator.Temp);
                using (reader)
                {

                    T outVal = new T();
                    MethodInfo method;
                    if (value is Enum)
                    {
                        method = typeof(ByteUnpacker).GetMethods().Single(x =>
                                x.Name == "ReadValuePacked" && x.IsGenericMethodDefinition)
                            .MakeGenericMethod(typeof(T));
                    }
                    else
                    {
                        method = typeof(ByteUnpacker).GetMethod("ReadValuePacked",
                            new[] {typeof(FastBufferReader).MakeByRefType(), typeof(T).MakeByRefType()});
                    }

                    object[] args = {reader, outVal};
                    method.Invoke(null, args);
                    outVal = (T) args[1];
                    Assert.AreEqual(value, outVal);
                    VerifyBytewiseEquality(value, outVal);
                }
            }
        }

        private unsafe void RunObjectTypeTest<T>(T value) where T : unmanaged
        {
            FastBufferWriter writer = new FastBufferWriter(sizeof(T)*2, Allocator.Temp);
            using (writer)
            {
                BytePacker.WriteObjectPacked(ref writer, value);
                var reader = new FastBufferReader(ref writer, Allocator.Temp);
                using (reader)
                {

                    ByteUnpacker.ReadObjectPacked(ref reader, out object outVal, typeof(T));
                    Assert.AreEqual(value, outVal);
                    VerifyBytewiseEquality(value, (T) outVal);
                }
            }
        }
        
        
        
        [Test]
        public void TestPacking64BitsUnsigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(9);
                ulong value = 0;
                BytePacker.WriteValuePacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);

                for (var i = 0; i < 64; ++i)
                {
                    value = 1UL << i;
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValuePacked(ref writer, value);
                    CheckUnsignedPackedSize64(ref writer, value);
                    CheckUnsignedPackedValue64(ref writer, value);
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1UL << i) | (1UL << j);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValuePacked(ref writer, value);
                        CheckUnsignedPackedSize64(ref writer, value);
                        CheckUnsignedPackedValue64(ref writer, value);
                    }
                }
            }
        }
        
        [Test]
        public void TestPacking32BitsUnsigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(9);
                uint value = 0;
                BytePacker.WriteValuePacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);

                for (var i = 0; i < 64; ++i)
                {
                    value = 1U << i;
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValuePacked(ref writer, value);
                    CheckUnsignedPackedSize32(ref writer, value);
                    CheckUnsignedPackedValue32(ref writer, value);
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1U << i) | (1U << j);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValuePacked(ref writer, value);
                        CheckUnsignedPackedSize32(ref writer, value);
                        CheckUnsignedPackedValue32(ref writer, value);
                    }
                }
            }
        }
        
        [Test]
        public void TestPacking64BitsSigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(9);
                long value = 0;
                BytePacker.WriteValuePacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);

                for (var i = 0; i < 64; ++i)
                {
                    value = 1L << i;
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValuePacked(ref writer, value);
                    CheckSignedPackedSize64(ref writer, value);
                    CheckSignedPackedValue64(ref writer, value);
                    
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValuePacked(ref writer, -value);
                    CheckSignedPackedSize64(ref writer, -value);
                    CheckSignedPackedValue64(ref writer, -value);
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1L << i) | (1L << j);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValuePacked(ref writer, value);
                        CheckSignedPackedSize64(ref writer, value);
                        CheckSignedPackedValue64(ref writer, value);
                        
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValuePacked(ref writer, -value);
                        CheckSignedPackedSize64(ref writer, -value);
                        CheckSignedPackedValue64(ref writer, -value);
                    }
                }
            }
        }
        
        [Test]
        public void TestPacking32BitsSigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(5);
                int value = 0;
                BytePacker.WriteValuePacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);

                for (var i = 0; i < 64; ++i)
                {
                    value = 1 << i;
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValuePacked(ref writer, value);
                    CheckSignedPackedSize32(ref writer, value);
                    CheckSignedPackedValue32(ref writer, value);
                    
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValuePacked(ref writer, -value);
                    CheckSignedPackedSize32(ref writer, -value);
                    CheckSignedPackedValue32(ref writer, -value);
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1 << i) | (1 << j);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValuePacked(ref writer, value);
                        CheckSignedPackedSize32(ref writer, value);
                        CheckSignedPackedValue32(ref writer, value);
                        
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValuePacked(ref writer, -value);
                        CheckSignedPackedSize32(ref writer, -value);
                        CheckSignedPackedValue32(ref writer, -value);
                    }
                }
            }
        }

        private int GetByteCount61Bits(ulong value)
        {
            
            if (value <= 0b0001_1111)
            {
                return 1;
            }

            if (value <= 0b0001_1111_1111_1111)
            {
                return 2;
            }

            if (value <= 0b0001_1111_1111_1111_1111_1111)
            {
                return 3;
            }

            if (value <= 0b0001_1111_1111_1111_1111_1111_1111_1111)
            {
                return 4;
            }

            if (value <= 0b0001_1111_1111_1111_1111_1111_1111_1111_1111_1111)
            {
                return 5;
            }

            if (value <= 0b0001_1111_1111_1111_1111_1111_1111_1111_1111_1111_1111_1111)
            {
                return 6;
            }

            if (value <= 0b0001_1111_1111_1111_1111_1111_1111_1111_1111_1111_1111_1111_1111_1111)
            {
                return 7;
            }

            return 8;
        }

        private int GetByteCount30Bits(uint value)
        {
            
            if (value <= 0b0011_1111)
            {
                return 1;
            }

            if (value <= 0b0011_1111_1111_1111)
            {
                return 2;
            }

            if (value <= 0b0011_1111_1111_1111_1111_1111)
            {
                return 3;
            }

            return 4;
        }

        private int GetByteCount15Bits(ushort value)
        {
            
            if (value <= 0b0111_1111)
            {
                return 1;
            }

            return 2;
        }

        private ulong Get61BitEncodedValue(ref FastBufferWriter writer)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValueBitPacked(ref reader, out ulong value);
                return value;
            }
        }

        private long Get60BitSignedEncodedValue(ref FastBufferWriter writer)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValueBitPacked(ref reader, out long value);
                return value;
            }
        }

        private uint Get30BitEncodedValue(ref FastBufferWriter writer)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValueBitPacked(ref reader, out uint value);
                return value;
            }
        }

        private int Get29BitSignedEncodedValue(ref FastBufferWriter writer)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValueBitPacked(ref reader, out int value);
                return value;
            }
        }

        private ushort Get15BitEncodedValue(ref FastBufferWriter writer)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValueBitPacked(ref reader, out ushort value);
                return value;
            }
        }

        private short Get14BitSignedEncodedValue(ref FastBufferWriter writer)
        {
            var reader = new FastBufferReader(ref writer, Allocator.Temp);
            using (reader)
            {
                ByteUnpacker.ReadValueBitPacked(ref reader, out short value);
                return value;
            }
        }
        
        [Test]
        public void TestBitPacking61BitsUnsigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(8);
                ulong value = 0;
                BytePacker.WriteValueBitPacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);
                Assert.AreEqual(0, writer.ToArray()[0] & 0b111);
                Assert.AreEqual(value, Get61BitEncodedValue(ref writer));
                
                for (var i = 0; i < 61; ++i)
                {
                    value = 1UL << i;
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount61Bits(value), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount61Bits(value)-1, writer.ToArray()[0] & 0b111, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get61BitEncodedValue(ref writer));
                    
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1UL << i) | (1UL << j);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount61Bits(value), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount61Bits(value)-1, writer.ToArray()[0] & 0b111, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get61BitEncodedValue(ref writer));
                    }
                }

                Assert.Throws<ArgumentException>(() => { BytePacker.WriteValueBitPacked(ref writer, 1UL << 61); });
            }
        }
        
        [Test]
        public void TestBitPacking60BitsSigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(8);
                long value = 0;
                BytePacker.WriteValueBitPacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);
                Assert.AreEqual(0, writer.ToArray()[0] & 0b111);
                Assert.AreEqual(value, Get60BitSignedEncodedValue(ref writer));
                
                for (var i = 0; i < 61; ++i)
                {
                    value = 1U << i;
                    ulong zzvalue = Arithmetic.ZigZagEncode(value);
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount61Bits(zzvalue), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount61Bits(zzvalue)-1, writer.ToArray()[0] & 0b111, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get60BitSignedEncodedValue(ref writer));

                    value = -value;
                    zzvalue = Arithmetic.ZigZagEncode(value);
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount61Bits(zzvalue), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount61Bits(zzvalue)-1, writer.ToArray()[0] & 0b111, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get60BitSignedEncodedValue(ref writer));
                    
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1U << i) | (1U << j);
                        zzvalue = Arithmetic.ZigZagEncode(value);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount61Bits(zzvalue), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount61Bits(zzvalue)-1, writer.ToArray()[0] & 0b111, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get60BitSignedEncodedValue(ref writer));

                        value = -value;
                        zzvalue = Arithmetic.ZigZagEncode(value);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount61Bits(zzvalue), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount61Bits(zzvalue)-1, writer.ToArray()[0] & 0b111, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get60BitSignedEncodedValue(ref writer));
                    }
                }

                Assert.Throws<ArgumentException>(() => { BytePacker.WriteValueBitPacked(ref writer, 1UL << 61); });
            }
        }
        
        [Test]
        public void TestBitPacking30BitsUnsigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(4);
                uint value = 0;
                BytePacker.WriteValueBitPacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);
                Assert.AreEqual(0, writer.ToArray()[0] & 0b11);
                Assert.AreEqual(value, Get30BitEncodedValue(ref writer));
                
                for (var i = 0; i < 30; ++i)
                {
                    value = 1U << i;
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount30Bits(value), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount30Bits(value)-1, writer.ToArray()[0] & 0b11, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get30BitEncodedValue(ref writer));
                    
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1U << i) | (1U << j);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount30Bits(value), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount30Bits(value)-1, writer.ToArray()[0] & 0b11, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get30BitEncodedValue(ref writer));
                    }
                }

                Assert.Throws<ArgumentException>(() => { BytePacker.WriteValueBitPacked(ref writer, 1U << 30); });
            }
        }
        
        [Test]
        public void TestBitPacking29BitsSigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(4);
                int value = 0;
                BytePacker.WriteValueBitPacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);
                Assert.AreEqual(0, writer.ToArray()[0] & 0b11);
                Assert.AreEqual(value, Get30BitEncodedValue(ref writer));
                
                for (var i = 0; i < 29; ++i)
                {
                    value = 1 << i;
                    uint zzvalue = (uint)Arithmetic.ZigZagEncode(value);
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount30Bits(zzvalue), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount30Bits(zzvalue)-1, writer.ToArray()[0] & 0b11, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get29BitSignedEncodedValue(ref writer));

                    value = -value;
                    zzvalue = (uint)Arithmetic.ZigZagEncode(value);
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount30Bits(zzvalue), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount30Bits(zzvalue)-1, writer.ToArray()[0] & 0b11, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get29BitSignedEncodedValue(ref writer));
                    
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (1 << i) | (1 << j);
                        zzvalue = (uint)Arithmetic.ZigZagEncode(value);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount30Bits(zzvalue), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount30Bits(zzvalue)-1, writer.ToArray()[0] & 0b11, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get29BitSignedEncodedValue(ref writer));

                        value = -value;
                        zzvalue = (uint)Arithmetic.ZigZagEncode(value);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount30Bits(zzvalue), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount30Bits(zzvalue)-1, writer.ToArray()[0] & 0b11, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get29BitSignedEncodedValue(ref writer));
                    }
                }
            }
        }
        
        [Test]
        public void TestBitPacking15BitsUnsigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(2);
                ushort value = 0;
                BytePacker.WriteValueBitPacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);
                Assert.AreEqual(0, writer.ToArray()[0] & 0b1);
                Assert.AreEqual(value, Get15BitEncodedValue(ref writer));
                
                for (var i = 0; i < 15; ++i)
                {
                    value = (ushort)(1U << i);
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount15Bits(value), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount15Bits(value)-1, writer.ToArray()[0] & 0b1, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get15BitEncodedValue(ref writer));
                    
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (ushort)((1U << i) | (1U << j));
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount15Bits(value), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount15Bits(value)-1, writer.ToArray()[0] & 0b1, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get15BitEncodedValue(ref writer));
                    }
                }

                Assert.Throws<ArgumentException>(() => { BytePacker.WriteValueBitPacked(ref writer, (ushort)(1U << 15)); });
            }
        }
        [Test]
        public void TestBitPacking14BitsSigned()
        {
            FastBufferWriter writer = new FastBufferWriter(9, Allocator.Temp);

            using (writer)
            {
                writer.VerifyCanWrite(2);
                short value = 0;
                BytePacker.WriteValueBitPacked(ref writer, value);
                Assert.AreEqual(1, writer.Position);
                Assert.AreEqual(0, writer.ToArray()[0] & 0b1);
                Assert.AreEqual(value, Get15BitEncodedValue(ref writer));
                
                for (var i = 0; i < 14; ++i)
                {
                    value = (short)(1 << i);
                    ushort zzvalue = (ushort)Arithmetic.ZigZagEncode(value);
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount15Bits(zzvalue), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount15Bits(zzvalue)-1, writer.ToArray()[0] & 0b1, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get14BitSignedEncodedValue(ref writer));

                    value = (short)-value;
                    zzvalue = (ushort)Arithmetic.ZigZagEncode(value);
                    writer.Seek(0);
                    writer.Truncate();
                    BytePacker.WriteValueBitPacked(ref writer, value);
                    Assert.AreEqual(GetByteCount15Bits(zzvalue), writer.Position, $"Failed on {value} ({i})");
                    Assert.AreEqual(GetByteCount15Bits(zzvalue)-1, writer.ToArray()[0] & 0b1, $"Failed on {value} ({i})");
                    Assert.AreEqual(value, Get14BitSignedEncodedValue(ref writer));
                    
                    for (var j = 0; j < 8; ++j)
                    {
                        value = (short)((1 << i) | (1 << j));
                        zzvalue = (ushort)Arithmetic.ZigZagEncode(value);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount15Bits(zzvalue), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount15Bits(zzvalue)-1, writer.ToArray()[0] & 0b1, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get14BitSignedEncodedValue(ref writer));

                        value = (short)-value;
                        zzvalue = (ushort)Arithmetic.ZigZagEncode(value);
                        writer.Seek(0);
                        writer.Truncate();
                        BytePacker.WriteValueBitPacked(ref writer, value);
                        Assert.AreEqual(GetByteCount15Bits(zzvalue), writer.Position, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(GetByteCount15Bits(zzvalue)-1, writer.ToArray()[0] & 0b1, $"Failed on {value} ({i}, {j})");
                        Assert.AreEqual(value, Get14BitSignedEncodedValue(ref writer));
                    }
                }
            }
        }
        
        [Test]
        public void TestPackingBasicTypes(
            [Values(typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
                typeof(long), typeof(ulong), typeof(bool), typeof(char), typeof(float), typeof(double),
                typeof(ByteEnum), typeof(SByteEnum), typeof(ShortEnum), typeof(UShortEnum), typeof(IntEnum),
                typeof(UIntEnum), typeof(LongEnum), typeof(ULongEnum), typeof(Vector2), typeof(Vector3), typeof(Vector4),
                typeof(Quaternion), typeof(Color), typeof(Color32), typeof(Ray), typeof(Ray2D))]
            Type testType,
            [Values] WriteType writeType)
        {
            var random = new Random();

            if (testType == typeof(byte))
            {
                byte b = (byte) random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(b);
                }
                else
                {
                    RunObjectTypeTest(b);
                }
            }
            else if (testType == typeof(sbyte))
            {
                sbyte sb = (sbyte) random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(sb);
                }
                else
                {
                    RunObjectTypeTest(sb);
                }
            }
            else if (testType == typeof(short))
            {
                short s = (short)random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(s);
                }
                else
                {
                    RunObjectTypeTest(s);
                }
            }
            else if (testType == typeof(ushort))
            {
                ushort us = (ushort)random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(us);
                }
                else
                {
                    RunObjectTypeTest(us);
                }
            }
            else if (testType == typeof(int))
            {
                int i = random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(i);
                }
                else
                {
                    RunObjectTypeTest(i);
                }
            }
            else if (testType == typeof(uint))
            {
                uint ui = (uint)random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(ui);
                }
                else
                {
                    RunObjectTypeTest(ui);
                }
            }
            else if (testType == typeof(long))
            {
                long l = ((long)random.Next() << 32) + random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(l);
                }
                else
                {
                    RunObjectTypeTest(l);
                }
            }
            else if (testType == typeof(ulong))
            {
                ulong ul = ((ulong)random.Next() << 32) + (ulong)random.Next();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(ul);
                }
                else
                {
                    RunObjectTypeTest(ul);
                }
            }
            else if (testType == typeof(bool))
            {
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(true);
                }
                else
                {
                    RunObjectTypeTest(true);
                }
            }
            else if (testType == typeof(char))
            {
                char c = 'a';
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(c);
                }
                else
                {
                    RunObjectTypeTest(c);
                }

                c = '\u263a';
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(c);
                }
                else
                {
                    RunObjectTypeTest(c);
                }
            }
            else if (testType == typeof(float))
            {
                float f = (float)random.NextDouble();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(f);
                }
                else
                {
                    RunObjectTypeTest(f);
                }
            }
            else if (testType == typeof(double))
            {
                double d = random.NextDouble();
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(d);
                }
                else
                {
                    RunObjectTypeTest(d);
                }
            }
            else if (testType == typeof(ByteEnum))
            {
                ByteEnum e = ByteEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(SByteEnum))
            {
                SByteEnum e = SByteEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(ShortEnum))
            {
                ShortEnum e = ShortEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(UShortEnum))
            {
                UShortEnum e = UShortEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(IntEnum))
            {
                IntEnum e = IntEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(UIntEnum))
            {
                UIntEnum e = UIntEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(LongEnum))
            {
                LongEnum e = LongEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(ULongEnum))
            {
                ULongEnum e = ULongEnum.C;
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(e);
                }
                else
                {
                    RunObjectTypeTest(e);
                }
            }
            else if (testType == typeof(Vector2))
            {
                var v = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(v);
                }
                else
                {
                    RunObjectTypeTest(v);
                }
            }
            else if (testType == typeof(Vector3))
            {
                var v = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(v);
                }
                else
                {
                    RunObjectTypeTest(v);
                }
            }
            else if (testType == typeof(Vector4))
            {
                var v = new Vector4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(v);
                }
                else
                {
                    RunObjectTypeTest(v);
                }
            }
            else if (testType == typeof(Quaternion))
            {
                var v = new Quaternion((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(v);
                }
                else
                {
                    RunObjectTypeTest(v);
                }
            }
            else if (testType == typeof(Color))
            {
                var v = new Color((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(v);
                }
                else
                {
                    RunObjectTypeTest(v);
                }
            }
            else if (testType == typeof(Color32))
            {
                var v = new Color32((byte)random.Next(), (byte)random.Next(), (byte)random.Next(), (byte)random.Next());
                if (writeType == WriteType.WriteDirect)
                {
                    RunTypeTest(v);
                }
                else
                {
                    RunObjectTypeTest(v);
                }
            }
            else if (testType == typeof(Ray))
            {
                // Rays need special handling on the equality checks because the constructor normalizes direction
                // Which can cause slight variations in the result
                var v = new Ray(
                    new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()), 
                    new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()));
                if (writeType == WriteType.WriteDirect)
                {
                    unsafe
                    {
                        FastBufferWriter writer = new FastBufferWriter(sizeof(Ray)*2, Allocator.Temp);
                        using (writer)
                        {
                            BytePacker.WriteValuePacked(ref writer, v);
                            var reader = new FastBufferReader(ref writer, Allocator.Temp);
                            using (reader)
                            {
                                ByteUnpacker.ReadValuePacked(ref reader, out Ray outVal);
                                Assert.AreEqual(v.origin, outVal.origin);
                                Assert.AreEqual(v.direction.x, outVal.direction.x, 0.00001);
                                Assert.AreEqual(v.direction.y, outVal.direction.y, 0.00001);
                                Assert.AreEqual(v.direction.z, outVal.direction.z, 0.00001);
                            }
                        }
                    }
                }
                else
                {
                    unsafe
                    {
                        FastBufferWriter writer = new FastBufferWriter(sizeof(Ray)*2, Allocator.Temp);
                        using (writer)
                        {
                            BytePacker.WriteObjectPacked(ref writer, v);
                            var reader = new FastBufferReader(ref writer, Allocator.Temp);
                            using (reader)
                            {
                                ByteUnpacker.ReadObjectPacked(ref reader, out object outVal, typeof(Ray));
                                Assert.AreEqual(v.origin, ((Ray) outVal).origin);
                                Assert.AreEqual(v.direction.x, ((Ray) outVal).direction.x, 0.00001);
                                Assert.AreEqual(v.direction.y, ((Ray) outVal).direction.y, 0.00001);
                                Assert.AreEqual(v.direction.z, ((Ray) outVal).direction.z, 0.00001);
                            }
                        }
                    }
                }
            }
            else if (testType == typeof(Ray2D))
            {
                // Rays need special handling on the equality checks because the constructor normalizes direction
                // Which can cause slight variations in the result
                var v = new Ray2D(
                    new Vector2((float)random.NextDouble(), (float)random.NextDouble()), 
                    new Vector2((float)random.NextDouble(), (float)random.NextDouble()));
                if (writeType == WriteType.WriteDirect)
                {
                    unsafe
                    {
                        FastBufferWriter writer = new FastBufferWriter(sizeof(Ray2D)*2, Allocator.Temp);
                        using (writer)
                        {
                            BytePacker.WriteValuePacked(ref writer, v);
                            var reader = new FastBufferReader(ref writer, Allocator.Temp);
                            using (reader)
                            {
                                ByteUnpacker.ReadValuePacked(ref reader, out Ray2D outVal);
                                Assert.AreEqual(v.origin, outVal.origin);
                                Assert.AreEqual(v.direction.x, outVal.direction.x, 0.00001);
                                Assert.AreEqual(v.direction.y, outVal.direction.y, 0.00001);
                            }
                        }
                    }
                }
                else
                {
                    unsafe
                    {
                        FastBufferWriter writer = new FastBufferWriter(sizeof(Ray2D)*2, Allocator.Temp);
                        using (writer)
                        {
                            BytePacker.WriteObjectPacked(ref writer, v);
                            var reader = new FastBufferReader(ref writer, Allocator.Temp);
                            using (reader)
                            {
                                ByteUnpacker.ReadObjectPacked(ref reader, out object outVal, typeof(Ray2D));
                                Assert.AreEqual(v.origin, ((Ray2D) outVal).origin);
                                Assert.AreEqual(v.direction.x, ((Ray2D) outVal).direction.x, 0.00001);
                                Assert.AreEqual(v.direction.y, ((Ray2D) outVal).direction.y, 0.00001);
                            }
                        }
                    }
                }
            }
            else
            {
                Assert.Fail("No type handler was provided for this type in the test!");
            }
        }
    }
}
