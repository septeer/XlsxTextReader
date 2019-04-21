﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace XlsxText
{
    public class XlsxTextCell
    {
        private string _reference = "A1", _value = "";
        /// <summary>
        /// Represents a single cell reference in a SpreadsheetML document
        /// </summary>
        public string Reference
        {
            get => _reference;
            private set
            {
                value = value?.Trim()?.ToUpper() ?? throw new ArgumentNullException(nameof(value));
                if (!Regex.IsMatch(value, @"^[A-Z]+\d+$"))
                    throw new Exception("Invalid value of cell reference");
                _reference = value;
            }
        }
        public string Value
        {
            get => _value;
            private set => _value = value ?? "";
        }

        /// <summary>
        /// Number of rows, starting from 1
        /// </summary>
        public int Row
        {
            get
            {
                return int.Parse(new Regex(@"\d+$").Match(Reference).Value);
            }
        }
        /// <summary>
        /// Number of columns, starting from 1
        /// </summary>
        public int Col
        {
            get
            {
                string value = new Regex(@"^[A-Z]+").Match(Reference).Value;
                int col = 0;
                for (int i = value.Length - 1, multiple = 1; i >= 0; --i, multiple *= 26)
                {
                    int n = value[i] - 'A' + 1;
                    col += (n * multiple);
                }
                return col;
            }
        }

        internal XlsxTextCell(string reference, string value)
        {
            Reference = reference;
            Value = value;
        }
    }

    public class XlsxTextSheetReader
    {
        public XlsxTextReader Archive { get; private set; }
        public string Name { get; private set; }

        private Dictionary<string, KeyValuePair<string, string>> _mergeCells = new Dictionary<string, KeyValuePair<string, string>>();
        public List<XlsxTextCell> Row { get; private set; } = new List<XlsxTextCell>();

        private XlsxTextSheetReader(XlsxTextReader archive, string name, ZipArchiveEntry archiveEntry)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (archiveEntry == null)
                throw new ArgumentNullException(nameof(archiveEntry));

            Archive = archive;
            Name = name;

            Load(archiveEntry);
        }

        public static XlsxTextSheetReader Create(XlsxTextReader archive, string name, ZipArchiveEntry archiveEntry)
        {
            return new XlsxTextSheetReader(archive, name, archiveEntry);
        }

        private void Load(ZipArchiveEntry archiveEntry)
        {
            _mergeCells.Clear();
            using (XmlReader mergeCellsReader = XmlReader.Create(archiveEntry.Open(), new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true }))
            {
                mergeCellsReader.MoveToContent();
                if (mergeCellsReader.NodeType == XmlNodeType.Element && mergeCellsReader.Name == "worksheet" && mergeCellsReader.ReadToDescendant("mergeCells") && mergeCellsReader.ReadToDescendant("mergeCell"))
                {
                    do
                    {
                        string[] references = mergeCellsReader["ref"].Split(':');
                        _mergeCells.Add(references[0], new KeyValuePair<string, string>(references[1], null));
                    } while (mergeCellsReader.ReadToNextSibling("mergeCell"));
                }
            }

            _reader = XmlReader.Create(archiveEntry.Open(), new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true });
            if (_reader.ReadToDescendant("worksheet") && _reader.ReadToDescendant("sheetData"))
            {
                _reader = _reader.ReadSubtree();
            }
        }

        private bool _isReading = false;
        private XmlReader _reader;
        public bool Read()
        {
            Row.Clear();
            if (_isReading ? _reader.ReadToNextSibling("row") : _reader.ReadToDescendant("row"))
            {
                _isReading = true;
                // read a row
                XmlReader rowReader = _reader.ReadSubtree();
                if (rowReader.ReadToDescendant("c"))
                {
                    do
                    {
                        string reference = rowReader["r"], style = rowReader["s"], type = rowReader["t"], value = null;
                        XmlReader cellReader = rowReader.ReadSubtree();

                        if (type == "inlineStr" && cellReader.ReadToDescendant("is") && cellReader.ReadToDescendant("t"))
                            value = cellReader.ReadElementContentAsString();
                        else if (cellReader.ReadToDescendant("v"))
                            value = cellReader.ReadElementContentAsString();
                        while (cellReader.Read()) ;

                        if (type == "d")
                            throw new Exception("can not parse the cell " + reference + "'s value of date type");
                        else if (type == "e")
                            throw new Exception("can not parse the cell " + reference + "'s value of error type");
                        else if (type == "s")
                            value = Archive.GetSharedString(int.Parse(value));
                        else if (type == "inlineStr")
                        {

                        }
                        else if (type == "b" && type == "n" && type == "str")
                        {

                        }
                        else
                        {
                            if (type == null)
                            {
                                // this is a mergeCell
                                if (value == null)
                                {
                                    XlsxTextCell cell = new XlsxTextCell(reference, "");
                                    int row = cell.Row, col = cell.Col;
                                    foreach (var mergeCell in _mergeCells)
                                    {
                                        XlsxTextCell begin = new XlsxTextCell(mergeCell.Key, "");
                                        if (row >= begin.Row && col >= begin.Col)
                                        {
                                            XlsxTextCell end = new XlsxTextCell(mergeCell.Value.Key, "");
                                            if (row <= end.Row && col <= end.Col)
                                                value = mergeCell.Value.Value != null ? mergeCell.Value.Value : "";
                                        }
                                    }
                                }
                                // this cell's value is NumberFormat
                                else if (style != null)
                                {
                                    throw new Exception("can not parse the cell " + reference + "'s value of NumberFormat type. Please replace with string type. ");
                                }
                            }
                        }

                        if (value == null) throw new Exception("can not parse the cell " + reference + "'s value");

                        if (_mergeCells.TryGetValue(reference, out _))
                            _mergeCells[reference] = new KeyValuePair<string, string>(_mergeCells[reference].Key, value);

                        Row.Add(new XlsxTextCell(reference, value));

                    } while (rowReader.ReadToNextSibling("c"));
                }
                return true;
            }

            return false;
        }
    }
    public class XlsxTextReader
    {
        public const string RelationshipPart = "xl/_rels/workbook.xml.rels";
        public const string WorkbookPart = "xl/workbook.xml";
        public const string SharedStringsPart = "xl/sharedStrings.xml";

        private ZipArchive _archive;
        private Dictionary<string, string> _rels = new Dictionary<string, string>();
        private List<KeyValuePair<string, string>> _sheets = new List<KeyValuePair<string, string>>();
        private List<string> _sharedStrings = new List<string>();

        public int SheetsCount => _sheets.Count;
        public string GetSharedString(int index) => 0 <= index && index < _sharedStrings.Count ? _sharedStrings[index] : null;
        private XlsxTextReader(Stream stream)
        {
            _archive = new ZipArchive(stream, ZipArchiveMode.Read);
            Load();
        }
        private XlsxTextReader(string path) : this(new FileStream(path, FileMode.Open))
        {
        }

        public static XlsxTextReader Create(Stream stream)
        {
            return new XlsxTextReader(stream);
        }
        public static XlsxTextReader Create(string path)
        {
            return new XlsxTextReader(path);
        }

        private void Load()
        {
            _rels.Clear();
            using (Stream stream = _archive.GetEntry("xl/_rels/workbook.xml.rels").Open())
            {
                using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true }))
                {
                    reader.MoveToContent();
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "Relationships" && reader.ReadToDescendant("Relationship"))
                    {
                        do
                        {
                            _rels.Add(reader["Id"], "xl/" + reader["Target"]);
                        } while (reader.ReadToNextSibling("Relationship"));
                    }
                }
            }

            _sheets.Clear();
            using (Stream stream = _archive.GetEntry(XlsxTextReader.WorkbookPart).Open())
            {
                using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true }))
                {
                    reader.MoveToContent();
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "workbook" && reader.ReadToDescendant("sheets") && reader.ReadToDescendant("sheet"))
                    {
                        do
                        {
                            _sheets.Add(new KeyValuePair<string, string>(reader["name"], _rels[reader["r:id"]]));
                        } while (reader.ReadToNextSibling("sheet"));
                    }
                }
            }

            _sharedStrings.Clear();
            using (Stream stream = _archive.GetEntry("xl/sharedStrings.xml")?.Open())
            {
                if (stream != null)
                {
                    using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true }))
                    {
                        reader.MoveToContent();
                        if (reader.NodeType == XmlNodeType.Element && reader.Name == "sst" && reader.ReadToDescendant("si"))
                        {
                            do
                            {
                                XmlReader inner = reader.ReadSubtree();
                                if (inner.Read() && inner.Read())
                                {
                                    if (inner.NodeType == XmlNodeType.Element && inner.Name == "t")
                                    {
                                        _sharedStrings.Add(inner.ReadElementContentAsString());
                                        while (inner.Read()) ;
                                    }
                                    else if (inner.NodeType == XmlNodeType.Element && inner.Name == "r")
                                    {
                                        string value = "";
                                        do
                                        {
                                            XmlReader inner2 = inner.ReadSubtree();
                                            if (inner2.ReadToDescendant("t"))
                                            {
                                                do
                                                {
                                                    value += inner2.ReadElementContentAsString();
                                                } while (inner2.ReadToNextSibling("t"));
                                            }
                                        } while (inner.ReadToNextSibling("r"));
                                        _sharedStrings.Add(value);
                                    }
                                }
                            } while (reader.ReadToNextSibling("si"));
                        }
                    }
                }
            }
        }

        public XlsxTextSheetReader SheetReader { get; private set; }
        private int _readIndex = 0;
        public bool Read()
        {
            SheetReader = null;
            if (_readIndex < SheetsCount)
            {
                // create a sheet reader
                SheetReader = XlsxTextSheetReader.Create(this, _sheets[_readIndex].Key, _archive.GetEntry(_sheets[_readIndex].Value));
                ++_readIndex;
                return true;
            }
            return false;
        }
    }
}
