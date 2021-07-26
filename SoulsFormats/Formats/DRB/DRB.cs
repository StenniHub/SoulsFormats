﻿using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace SoulsFormats
{
    /// <summary>
    /// A UI configuration format used in DS1.
    /// </summary>
    public partial class DRB
    {
        /// <summary>
        /// If true, Shapes have scaling information.
        /// </summary>
        public bool DSR { get; set; }

        /// <summary>
        /// Available UI textures for reference; not actually necessary for textures to work.
        /// </summary>
        public List<Texture> Textures { get; set; }

        /// <summary>
        /// Unknown data used by Aniks.
        /// </summary>
        public byte[] AnipBytes { get; set; }

        /// <summary>
        /// Unknown data used by Aniks.
        /// </summary>
        public byte[] IntpBytes { get; set; }

        /// <summary>
        /// Unknown.
        /// </summary>
        public List<Anim> Anims { get; set; }

        /// <summary>
        /// Unknown.
        /// </summary>
        public List<Scdl> Scdls { get; set; }

        /// <summary>
        /// Groups of UI elements.
        /// </summary>
        public List<Dlg> Dlgs { get; set; }

        private const int ANIK_SIZE = 0x20;
        private const int ANIO_SIZE = 0x10;
        private const int SCDK_SIZE = 0x20;
        private const int SCDO_SIZE = 0x10;
        private const int DLGO_SIZE = 0x20;

        private static bool Is(BinaryReaderEx br)
        {
            if (br.Length < 4)
                return false;

            string magic = br.GetASCII(0, 4);
            return magic == "DRB\0";
        }

        private void Read(BinaryReaderEx br, bool dsr)
        {
            br.BigEndian = false;
            DSR = dsr;

            ReadNullBlock(br, "DRB\0");
            Dictionary<int, string> strings = ReadSTR(br);
            Textures = ReadTEXI(br, strings);
            long shprStart = ReadBlobBlock(br, "SHPR");
            long ctprStart = ReadBlobBlock(br, "CTPR");
            AnipBytes = ReadBlobBytes(br, "ANIP");
            IntpBytes = ReadBlobBytes(br, "INTP");
            long scdpStart = ReadBlobBlock(br, "SCDP");
            Dictionary<int, Shape> shapes = ReadSHAP(br, dsr, strings, shprStart);
            Dictionary<int, Control> controls = ReadCTRL(br, strings, ctprStart);
            Dictionary<int, Anik> aniks = ReadANIK(br, strings);
            Dictionary<int, Anio> anios = ReadANIO(br, aniks);
            Anims = ReadANIM(br, strings, anios);
            Dictionary<int, Scdk> scdks = ReadSCDK(br, strings, scdpStart);
            Dictionary<int, Scdo> scdos = ReadSCDO(br, strings, scdks);
            Scdls = ReadSCDL(br, strings, scdos);
            Dictionary<int, Dlgo> dlgos = ReadDLGO(br, strings, shapes, controls);
            Dlgs = ReadDLG(br, strings, shapes, controls, dlgos);
            ReadNullBlock(br, "END\0");

            void CheckShape(Shape shape)
            {
                if (shape is Shape.Dialog dialog)
                {
                    if (dialog.DlgIndex != -1)
                        dialog.Dlg = Dlgs[dialog.DlgIndex];
                }
            }

            foreach (Dlg dlg in Dlgs)
            {
                CheckShape(dlg.Shape);
                foreach (Dlgo dlgo in dlg.Dlgos)
                    CheckShape(dlgo.Shape);
            }
        }

        private void Write(BinaryWriterEx bw)
        {
            void CheckShape(Shape shape)
            {
                if (shape is Shape.Dialog dialog)
                {
                    if (dialog.Dlg == null)
                        dialog.DlgIndex = -1;
                    else if (Dlgs.Contains(dialog.Dlg))
                        dialog.DlgIndex = (short)Dlgs.IndexOf(dialog.Dlg);
                    else
                        throw new InvalidDataException($"Dlg \"{dialog.Dlg.Name}\" is referenced but no found in Dlgs list.");
                }
            }

            foreach (Dlg dlg in Dlgs)
            {
                CheckShape(dlg.Shape);
                foreach (Dlgo dlgo in dlg.Dlgos)
                    CheckShape(dlgo.Shape);
            }

            bw.BigEndian = false;

            WriteNullBlock(bw, "DRB\0");
            Dictionary<string, int> stringOffsets = WriteSTR(bw);
            WriteTEXI(bw, stringOffsets);
            Queue<int> shprOffsets = WriteSHPR(bw, DSR, stringOffsets);
            Queue<int> ctprOffsets = WriteCTPR(bw);
            WriteBlobBytes(bw, "ANIP", AnipBytes);
            WriteBlobBytes(bw, "INTP", IntpBytes);
            Queue<int> scdpOffsets = WriteSCDP(bw);
            Queue<int> shapOffsets = WriteSHAP(bw, stringOffsets, shprOffsets);
            Queue<int> ctrlOffsets = WriteCTRL(bw, stringOffsets, ctprOffsets);
            Queue<int> anikOffsets = WriteANIK(bw, stringOffsets);
            Queue<int> anioOffsets = WriteANIO(bw, anikOffsets);
            WriteANIM(bw, stringOffsets, anioOffsets);
            Queue<int> scdkOffsets = WriteSCDK(bw, stringOffsets, scdpOffsets);
            Queue<int> scdoOffsets = WriteSCDO(bw, stringOffsets, scdkOffsets);
            WriteSCDL(bw, stringOffsets, scdoOffsets);
            Queue<int> dlgoOffsets = WriteDLGO(bw, stringOffsets, shapOffsets, ctrlOffsets);
            WriteDLG(bw, stringOffsets, shapOffsets, ctrlOffsets, dlgoOffsets);
            WriteNullBlock(bw, "END\0");
        }

        /// <summary>
        /// Returns the element group with the given name, or null if not found.
        /// </summary>
        public Dlg this[string name] => Dlgs.Find(dlg => dlg.Name == name);

        #region Read/write methods
        private Dictionary<int, string> ReadSTR(BinaryReaderEx br)
        {
            long start = ReadBlockHeader(br, "STR\0", out int count);
            var strings = new Dictionary<int, string>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                strings[offset] = br.ReadUTF16();
            }
            br.Pad(0x10);
            return strings;
        }

        private Dictionary<string, int> WriteSTR(BinaryWriterEx bw)
        {
            long start = WriteBlockHeader(bw, "STR\0");
            var stringOffsets = new Dictionary<string, int>();
            void writeString(string str)
            {
                if (!stringOffsets.ContainsKey(str))
                {
                    long offset = bw.Position - start;
                    bw.WriteUTF16(str, true);
                    stringOffsets[str] = (int)offset;
                }
            }

            foreach (Texture tex in Textures)
            {
                writeString(tex.Name);
                writeString(tex.Path);
            }

            foreach (Anim anim in Anims)
            {
                writeString(anim.Name);
                foreach (Anio anio in anim.Anios)
                {
                    foreach (Anik anik in anio.Aniks)
                    {
                        writeString(anik.Name);
                    }
                }
            }

            foreach (Scdl scdl in Scdls)
            {
                writeString(scdl.Name);
                foreach (Scdo scdo in scdl.Scdos)
                {
                    writeString(scdo.Name);
                    foreach (Scdk scdk in scdo.Scdks)
                    {
                        writeString(scdk.Name);
                    }
                }
            }

            void writeShapeStrings(Shape shape)
            {
                writeString(shape.Type.ToString());
                if (shape is Shape.ScrollText scrollText && scrollText.TextType == Shape.TxtType.Literal)
                {
                    writeString(scrollText.TextLiteral);
                }
                else if (shape is Shape.Text text && text.TextType == Shape.TxtType.Literal)
                {
                    writeString(text.TextLiteral);
                }
            }

            foreach (Dlg dlg in Dlgs)
            {
                writeString(dlg.Name);
                writeShapeStrings(dlg.Shape);
                writeString(dlg.Control.Type.ToString());
                foreach (Dlgo dlgo in dlg.Dlgos)
                {
                    writeString(dlgo.Name);
                    writeShapeStrings(dlgo.Shape);
                    writeString(dlgo.Control.Type.ToString());
                }
            }

            FinishBlockHeader(bw, "STR\0", start, stringOffsets.Count);
            return stringOffsets;
        }

        private List<Texture> ReadTEXI(BinaryReaderEx br, Dictionary<int, string> strings)
        {
            ReadBlockHeader(br, "TEXI", out int count);
            var textures = new List<Texture>(count);
            for (int i = 0; i < count; i++)
            {
                textures.Add(new Texture(br, strings));
            }
            br.Pad(0x10);
            return textures;
        }

        private void WriteTEXI(BinaryWriterEx bw, Dictionary<string, int> stringOffsets)
        {
            long start = WriteBlockHeader(bw, "TEXI");
            foreach (Texture tex in Textures)
                tex.Write(bw, stringOffsets);
            FinishBlockHeader(bw, "TEXI", start, Textures.Count);
        }

        private Queue<int> WriteSHPR(BinaryWriterEx bw, bool dsr, Dictionary<string, int> stringOffsets)
        {
            long start = WriteBlobBlock(bw, "SHPR");
            var shprOffsets = new Queue<int>();
            void writeShape(Shape shape)
            {
                int offset = (int)(bw.Position - start);
                shprOffsets.Enqueue(offset);
                shape.WriteData(bw, dsr, stringOffsets);
            }

            foreach (Dlg dlg in Dlgs)
            {
                foreach (Dlgo dlgo in dlg.Dlgos)
                {
                    writeShape(dlgo.Shape);
                }
            }

            foreach (Dlg dlg in Dlgs)
            {
                writeShape(dlg.Shape);
            }

            FinishBlobBlock(bw, "SHPR", start);
            return shprOffsets;
        }

        private Queue<int> WriteCTPR(BinaryWriterEx bw)
        {
            long start = WriteBlobBlock(bw, "CTPR");
            var ctprOffsets = new Queue<int>();
            void writeControl(Control control)
            {
                int offset = (int)(bw.Position - start);
                ctprOffsets.Enqueue(offset);
                control.WriteData(bw);
            }

            foreach (Dlg dlg in Dlgs)
            {
                foreach (Dlgo dlgo in dlg.Dlgos)
                {
                    writeControl(dlgo.Control);
                }
            }

            foreach (Dlg dlg in Dlgs)
            {
                writeControl(dlg.Control);
            }

            FinishBlobBlock(bw, "CTPR", start);
            return ctprOffsets;
        }

        private Queue<int> WriteSCDP(BinaryWriterEx bw)
        {
            long start = WriteBlobBlock(bw, "SCDP");
            var scdpOffsets = new Queue<int>();

            foreach (Scdl scdl in Scdls)
            {
                foreach (Scdo scdo in scdl.Scdos)
                {
                    foreach (Scdk scdk in scdo.Scdks)
                    {
                        int offset = (int)(bw.Position - start);
                        scdpOffsets.Enqueue(offset);
                        scdk.WriteSCDP(bw);
                    }
                }
            }

            FinishBlobBlock(bw, "SCDP", start);
            return scdpOffsets;
        }

        private Dictionary<int, Shape> ReadSHAP(BinaryReaderEx br, bool dsr, Dictionary<int, string> strings, long shprStart)
        {
            long start = ReadBlockHeader(br, "SHAP", out int count);
            var shapes = new Dictionary<int, Shape>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                shapes[offset] = Shape.Read(br, dsr, strings, shprStart);
            }
            br.Pad(0x10);
            return shapes;
        }

        private Queue<int> WriteSHAP(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> shprOffsets)
        {
            long start = WriteBlockHeader(bw, "SHAP");
            var shapOffsets = new Queue<int>();
            void writeShape(Shape shape)
            {
                int offset = (int)(bw.Position - start);
                shapOffsets.Enqueue(offset);
                shape.WriteHeader(bw, stringOffsets, shprOffsets);
            }

            foreach (Dlg dlg in Dlgs)
            {
                foreach (Dlgo dlgo in dlg.Dlgos)
                {
                    writeShape(dlgo.Shape);
                }
            }

            foreach (Dlg dlg in Dlgs)
            {
                writeShape(dlg.Shape);
            }

            FinishBlockHeader(bw, "SHAP", start, shapOffsets.Count);
            return shapOffsets;
        }

        private Dictionary<int, Control> ReadCTRL(BinaryReaderEx br, Dictionary<int, string> strings, long ctprStart)
        {
            long start = ReadBlockHeader(br, "CTRL", out int count);
            var controls = new Dictionary<int, Control>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                controls[offset] = Control.Read(br, strings, ctprStart);
            }
            br.Pad(0x10);
            return controls;
        }

        private Queue<int> WriteCTRL(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> ctprOffsets)
        {
            long start = WriteBlockHeader(bw, "CTRL");
            var ctrlOffsets = new Queue<int>();
            void writeControl(Control control)
            {
                int offset = (int)(bw.Position - start);
                ctrlOffsets.Enqueue(offset);
                control.WriteHeader(bw, stringOffsets, ctprOffsets);
            }

            foreach (Dlg dlg in Dlgs)
            {
                foreach (Dlgo dlgo in dlg.Dlgos)
                {
                    writeControl(dlgo.Control);
                }
            }

            foreach (Dlg dlg in Dlgs)
            {
                writeControl(dlg.Control);
            }

            FinishBlockHeader(bw, "CTRL", start, ctrlOffsets.Count);
            return ctrlOffsets;
        }

        private Dictionary<int, Anik> ReadANIK(BinaryReaderEx br, Dictionary<int, string> strings)
        {
            long start = ReadBlockHeader(br, "ANIK", out int count);
            var aniks = new Dictionary<int, Anik>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                aniks[offset] = new Anik(br, strings);
            }
            br.Pad(0x10);
            return aniks;
        }

        private Queue<int> WriteANIK(BinaryWriterEx bw, Dictionary<string, int> stringOffsets)
        {
            long start = WriteBlockHeader(bw, "ANIK");
            int count = 0;
            var anikOffsets = new Queue<int>();

            foreach (Anim anim in Anims)
            {
                foreach (Anio anio in anim.Anios)
                {
                    int offset = (int)(bw.Position - start);
                    anikOffsets.Enqueue(offset);
                    count += anio.Aniks.Count;
                    foreach (Anik anik in anio.Aniks)
                        anik.Write(bw, stringOffsets);
                }
            }

            FinishBlockHeader(bw, "ANIK", start, count);
            return anikOffsets;
        }

        private Dictionary<int, Anio> ReadANIO(BinaryReaderEx br, Dictionary<int, Anik> aniks)
        {
            long start = ReadBlockHeader(br, "ANIO", out int count);
            var anios = new Dictionary<int, Anio>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                anios[offset] = new Anio(br, aniks);
            }
            br.Pad(0x10);
            return anios;
        }

        private Queue<int> WriteANIO(BinaryWriterEx bw, Queue<int> anikOffsets)
        {
            long start = WriteBlockHeader(bw, "ANIO");
            int count = 0;
            var anioOffsets = new Queue<int>();

            foreach (Anim anim in Anims)
            {
                int offset = (int)(bw.Position - start);
                anioOffsets.Enqueue(offset);
                count += anim.Anios.Count;
                foreach (Anio anio in anim.Anios)
                    anio.Write(bw, anikOffsets);
            }

            FinishBlockHeader(bw, "ANIO", start, count);
            return anioOffsets;
        }

        private List<Anim> ReadANIM(BinaryReaderEx br, Dictionary<int, string> strings, Dictionary<int, Anio> anios)
        {
            ReadBlockHeader(br, "ANIM", out int count);
            var anims = new List<Anim>(count);
            for (int i = 0; i < count; i++)
            {
                anims.Add(new Anim(br, strings, anios));
            }
            br.Pad(0x10);
            return anims;
        }

        private void WriteANIM(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> anioOffsets)
        {
            long start = WriteBlockHeader(bw, "ANIM");
            foreach (Anim anim in Anims)
            {
                anim.Write(bw, stringOffsets, anioOffsets);
            }
            FinishBlockHeader(bw, "ANIM", start, Anims.Count);
        }

        private Dictionary<int, Scdk> ReadSCDK(BinaryReaderEx br, Dictionary<int, string> strings, long scdpStart)
        {
            long start = ReadBlockHeader(br, "SCDK", out int count);
            var scdks = new Dictionary<int, Scdk>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                scdks[offset] = new Scdk(br, strings, scdpStart);
            }
            br.Pad(0x10);
            return scdks;
        }

        private Queue<int> WriteSCDK(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> scdpOffsets)
        {
            long start = WriteBlockHeader(bw, "SCDK");
            int count = 0;
            var scdkOffsets = new Queue<int>();

            foreach (Scdl scdl in Scdls)
            {
                foreach (Scdo scdo in scdl.Scdos)
                {
                    int offset = (int)(bw.Position - start);
                    scdkOffsets.Enqueue(offset);
                    count += scdo.Scdks.Count;
                    foreach (Scdk scdk in scdo.Scdks)
                        scdk.Write(bw, stringOffsets, scdpOffsets);
                }
            }

            FinishBlockHeader(bw, "SCDK", start, count);
            return scdkOffsets;
        }

        private Dictionary<int, Scdo> ReadSCDO(BinaryReaderEx br, Dictionary<int, string> strings, Dictionary<int, Scdk> scdks)
        {
            long start = ReadBlockHeader(br, "SCDO", out int count);
            var scdos = new Dictionary<int, Scdo>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                scdos[offset] = new Scdo(br, strings, scdks);
            }
            br.Pad(0x10);
            return scdos;
        }

        private Queue<int> WriteSCDO(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> scdkOffsets)
        {
            long start = WriteBlockHeader(bw, "SCDO");
            int count = 0;
            var scdoOffsets = new Queue<int>();

            foreach (Scdl scdl in Scdls)
            {
                int offset = (int)(bw.Position - start);
                scdoOffsets.Enqueue(offset);
                count += scdl.Scdos.Count;
                foreach (Scdo scdo in scdl.Scdos)
                    scdo.Write(bw, stringOffsets, scdkOffsets);
            }

            FinishBlockHeader(bw, "SCDO", start, count);
            return scdoOffsets;
        }

        private List<Scdl> ReadSCDL(BinaryReaderEx br, Dictionary<int, string> strings, Dictionary<int, Scdo> scdos)
        {
            ReadBlockHeader(br, "SCDL", out int count);
            var scdls = new List<Scdl>(count);
            for (int i = 0; i < count; i++)
            {
                scdls.Add(new Scdl(br, strings, scdos));
            }
            br.Pad(0x10);
            return scdls;
        }

        private void WriteSCDL(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> scdoOffsets)
        {
            long start = WriteBlockHeader(bw, "SCDL");
            foreach (Scdl scdl in Scdls)
            {
                scdl.Write(bw, stringOffsets, scdoOffsets);
            }
            FinishBlockHeader(bw, "SCDL", start, Scdls.Count);
        }

        private Dictionary<int, Dlgo> ReadDLGO(BinaryReaderEx br, Dictionary<int, string> strings, Dictionary<int, Shape> shapes, Dictionary<int, Control> controls)
        {
            long start = ReadBlockHeader(br, "DLGO", out int count);
            var dlgos = new Dictionary<int, Dlgo>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(br.Position - start);
                dlgos[offset] = new Dlgo(br, strings, shapes, controls);
            }
            br.Pad(0x10);
            return dlgos;
        }

        private Queue<int> WriteDLGO(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> shapOffsets, Queue<int> ctrlOffsets)
        {
            long start = WriteBlockHeader(bw, "DLGO");
            int count = 0;
            var dlgoOffsets = new Queue<int>();

            foreach (Dlg dlg in Dlgs)
            {
                int offset = (int)(bw.Position - start);
                dlgoOffsets.Enqueue(offset);
                count += dlg.Dlgos.Count;
                foreach (Dlgo dlgo in dlg.Dlgos)
                    dlgo.Write(bw, stringOffsets, shapOffsets, ctrlOffsets);
            }

            FinishBlockHeader(bw, "DLGO", start, count);
            return dlgoOffsets;
        }

        private List<Dlg> ReadDLG(BinaryReaderEx br, Dictionary<int, string> strings, Dictionary<int, Shape> shapes, Dictionary<int, Control> controls, Dictionary<int, Dlgo> dlgos)
        {
            ReadBlockHeader(br, "DLG\0", out int count);
            var dlgs = new List<Dlg>(count);
            for (int i = 0; i < count; i++)
            {
                dlgs.Add(new Dlg(br, strings, shapes, controls, dlgos));
            }
            br.Pad(0x10);
            return dlgs;
        }

        private void WriteDLG(BinaryWriterEx bw, Dictionary<string, int> stringOffsets, Queue<int> shapOffsets, Queue<int> ctrlOffsets, Queue<int> dlgoOffsets)
        {
            long start = WriteBlockHeader(bw, "DLG\0");
            foreach (Dlg dlg in Dlgs)
            {
                dlg.Write(bw, stringOffsets, shapOffsets, ctrlOffsets, dlgoOffsets);
            }
            FinishBlockHeader(bw, "DLG\0", start, Dlgs.Count);
        }
        #endregion

        #region Read/write utilities
        private static void ReadNullBlock(BinaryReaderEx br, string name)
        {
            br.AssertASCII(name);
            br.AssertInt32(0);
            br.AssertInt32(0);
            br.AssertInt32(0);
        }

        private static void WriteNullBlock(BinaryWriterEx bw, string name)
        {
            bw.WriteASCII(name);
            bw.WriteInt32(0);
            bw.WriteInt32(0);
            bw.WriteInt32(0);
        }

        private static long ReadBlobBlock(BinaryReaderEx br, string name)
        {
            br.AssertASCII(name);
            int size = br.ReadInt32();
            br.AssertInt32(1);
            br.AssertInt32(0);

            long start = br.Position;
            br.Skip(size);
            br.Pad(0x10);
            return start;
        }

        private static long WriteBlobBlock(BinaryWriterEx bw, string name)
        {
            bw.WriteASCII(name);
            bw.ReserveInt32($"BlobBlockSize{name}");
            bw.WriteInt32(1);
            bw.WriteInt32(0);
            return bw.Position;
        }

        private static void FinishBlobBlock(BinaryWriterEx bw, string name, long start)
        {
            bw.Pad(0x10);
            bw.FillInt32($"BlobBlockSize{name}", (int)(bw.Position - start));
        }

        private static byte[] ReadBlobBytes(BinaryReaderEx br, string name)
        {
            br.AssertASCII(name);
            int size = br.ReadInt32();
            br.AssertInt32(1);
            br.AssertInt32(0);

            byte[] bytes = br.ReadBytes(size);
            br.Pad(0x10);
            return bytes;
        }

        private static void WriteBlobBytes(BinaryWriterEx bw, string name, byte[] bytes)
        {
            bw.WriteASCII(name);
            bw.ReserveInt32("BlobSize");
            bw.WriteInt32(1);
            bw.WriteInt32(0);

            long start = bw.Position;
            bw.WriteBytes(bytes);
            bw.Pad(0x10);
            bw.FillInt32("BlobSize", (int)(bw.Position - start));
        }

        private static long ReadBlockHeader(BinaryReaderEx br, string name, out int count)
        {
            br.AssertASCII(name);
            int size = br.ReadInt32();
            count = br.ReadInt32();
            br.AssertInt32(0);
            return br.Position;
        }

        private static long WriteBlockHeader(BinaryWriterEx bw, string name)
        {
            bw.WriteASCII(name);
            bw.ReserveInt32($"BlockSize{name}");
            bw.ReserveInt32($"BlockCount{name}");
            bw.WriteInt32(0);
            return bw.Position;
        }

        private static void FinishBlockHeader(BinaryWriterEx bw, string name, long start, int count)
        {
            bw.Pad(0x10);
            bw.FillInt32($"BlockSize{name}", (int)(bw.Position - start));
            bw.FillInt32($"BlockCount{name}", count);
        }

        private static Color ReadABGR(BinaryReaderEx br)
        {
            byte[] abgr = br.ReadBytes(4);
            return Color.FromArgb(abgr[0], abgr[3], abgr[2], abgr[1]);
        }

        private static void WriteABGR(BinaryWriterEx bw, Color color)
        {
            bw.WriteByte(color.A);
            bw.WriteByte(color.B);
            bw.WriteByte(color.G);
            bw.WriteByte(color.R);
        }
        #endregion

        #region SoulsFile boilerplate
        /// <summary>
        /// The type of DCX compression to be used when writing.
        /// </summary>
        public DCX.Type Compression = DCX.Type.None;

        /// <summary>
        /// Returns true if the bytes appear to be a DRB.
        /// </summary>
        public static bool Is(byte[] bytes)
        {
            if (bytes.Length == 0)
                return false;

            BinaryReaderEx br = new BinaryReaderEx(false, bytes);
            return Is(SFUtil.GetDecompressedBR(br, out _));
        }

        /// <summary>
        /// Returns true if the file appears to be a DRB.
        /// </summary>
        public static bool Is(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                if (stream.Length == 0)
                    return false;

                BinaryReaderEx br = new BinaryReaderEx(false, stream);
                return Is(SFUtil.GetDecompressedBR(br, out _));
            }
        }

        /// <summary>
        /// Loads a DRB from a byte array, automatically decompressing it if necessary.
        /// </summary>
        public static DRB Read(byte[] bytes, bool dsr)
        {
            BinaryReaderEx br = new BinaryReaderEx(false, bytes);
            DRB drb = new DRB();
            br = SFUtil.GetDecompressedBR(br, out drb.Compression);
            drb.Read(br, dsr);
            return drb;
        }

        /// <summary>
        /// Loads a DRB from the specified path, automatically decompressing it if necessary.
        /// </summary>
        public static DRB Read(string path, bool dsr)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                BinaryReaderEx br = new BinaryReaderEx(false, stream);
                DRB drb = new DRB();
                br = SFUtil.GetDecompressedBR(br, out drb.Compression);
                drb.Read(br, dsr);
                return drb;
            }
        }

        /// <summary>
        /// Writes file data to a BinaryWriterEx, compressing it afterwards if specified.
        /// </summary>
        private void Write(BinaryWriterEx bw, DCX.Type compression)
        {
            if (compression == DCX.Type.None)
            {
                Write(bw);
            }
            else
            {
                BinaryWriterEx bwUncompressed = new BinaryWriterEx(false);
                Write(bwUncompressed);
                byte[] uncompressed = bwUncompressed.FinishBytes();
                DCX.Compress(uncompressed, bw, compression);
            }
        }

        /// <summary>
        /// Writes the file to an array of bytes, automatically compressing it if necessary.
        /// </summary>
        public byte[] Write()
        {
            return Write(Compression);
        }

        /// <summary>
        /// Writes the file to an array of bytes, compressing it as specified.
        /// </summary>
        public byte[] Write(DCX.Type compression)
        {
            BinaryWriterEx bw = new BinaryWriterEx(false);
            Write(bw, compression);
            return bw.FinishBytes();
        }

        /// <summary>
        /// Writes the file to the specified path, automatically compressing it if necessary.
        /// </summary>
        public void Write(string path)
        {
            Write(path, Compression);
        }

        /// <summary>
        /// Writes the file to the specified path, compressing it as specified.
        /// </summary>
        public void Write(string path, DCX.Type compression)
        {
            using (FileStream stream = File.Create(path))
            {
                BinaryWriterEx bw = new BinaryWriterEx(false, stream);
                Write(bw, compression);
                bw.Finish();
            }
        }
        #endregion
    }
}
