using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// Reads who made an audio file from the tags inside it (Unity's importer doesn't keep them): ID3 (MP3: the
    /// artist, or the composer), Vorbis comments (OGG: ARTIST, or COMPOSER) and the RIFF INFO list (WAV: IART). Only the
    /// start of the file is read (and an MP3's last 128 bytes, for old ID3v1 tags); answers are kept until the file
    /// changes. Null when the file doesn't say.
    /// </summary>
    public static class AudioMetadata
    {
        private const int ReadLimit = 256 * 1024;
        private static readonly Dictionary<string, (DateTime modified, string artist)> _cache = new();

        /// <summary>The creator named in an audio file ("Assets/..." or a full path), or null.</summary>
        public static string ArtistOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            try
            {
                string full = Path.GetFullPath(assetPath);
                if (!File.Exists(full))
                    return null;
                var modified = File.GetLastWriteTimeUtc(full);
                if (_cache.TryGetValue(full, out var cached) && cached.modified == modified)
                    return cached.artist;
                string artist = Read(full);
                artist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim().Trim('\0').Trim();
                _cache[full] = (modified, artist);
                return artist;
            }
            catch (Exception)
            {
                return null; // an unreadable or odd file just has no creator shown
            }
        }

        private static string Read(string path)
        {
            byte[] head;
            using (var stream = File.OpenRead(path))
            {
                head = new byte[(int)Math.Min(stream.Length, ReadLimit)];
                int read = 0;
                while (read < head.Length)
                {
                    int n = stream.Read(head, read, head.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3":
                    return Id3v2(head) ?? Id3v1(path);
                case ".ogg":
                    return Vorbis(head);
                case ".wav":
                    return RiffInfo(head);
                default:
                    return Id3v2(head);
            }
        }

        #region ID3 (MP3)

        private static string Id3v2(byte[] data)
        {
            if (data.Length < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
                return null;
            int version = data[3];
            int size = Syncsafe(data, 6);
            int end = Math.Min(data.Length, 10 + size);
            int pos = 10;
            if ((data[5] & 0x40) != 0 && version >= 3 && pos + 4 <= end) // extended header
                pos += version == 4 ? Syncsafe(data, pos) : BigEndian(data, pos) + 4;

            string artist = null, composer = null;
            while (pos < end)
            {
                string id;
                int frameSize, header;
                if (version == 2)
                {
                    if (pos + 6 > end) break;
                    id = Encoding.ASCII.GetString(data, pos, 3);
                    frameSize = (data[pos + 3] << 16) | (data[pos + 4] << 8) | data[pos + 5];
                    header = 6;
                }
                else
                {
                    if (pos + 10 > end) break;
                    id = Encoding.ASCII.GetString(data, pos, 4);
                    frameSize = version == 4 ? Syncsafe(data, pos + 4) : BigEndian(data, pos + 4);
                    header = 10;
                }
                if (id[0] == '\0' || frameSize <= 0 || pos + header + frameSize > end)
                    break;
                if (id == "TPE1" || id == "TP1")
                    artist ??= Id3Text(data, pos + header, frameSize);
                else if (id == "TCOM" || id == "TCM")
                    composer ??= Id3Text(data, pos + header, frameSize);
                pos += header + frameSize;
            }
            return artist ?? composer;
        }

        // A text frame: an encoding byte, then the text (ID3v2.4 may list several values, separated by nulls).
        private static string Id3Text(byte[] data, int start, int length)
        {
            if (length < 2) return null;
            int encoding = data[start];
            int offset = start + 1, count = length - 1;
            string text = encoding switch
            {
                0 => Latin1(data, offset, count),
                1 => Utf16WithBom(data, offset, count),
                2 => Encoding.BigEndianUnicode.GetString(data, offset, count),
                3 => Encoding.UTF8.GetString(data, offset, count),
                _ => null,
            };
            if (text == null) return null;
            text = text.Replace("\0", " / ").Trim().TrimEnd('/').Trim();
            return text.Length > 0 ? text : null;
        }

        private static string Utf16WithBom(byte[] data, int offset, int count)
        {
            if (count >= 2 && data[offset] == 0xFE && data[offset + 1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data, offset + 2, count - 2);
            if (count >= 2 && data[offset] == 0xFF && data[offset + 1] == 0xFE)
                return Encoding.Unicode.GetString(data, offset + 2, count - 2);
            return Encoding.Unicode.GetString(data, offset, count);
        }

        // ID3v1: the last 128 bytes, "TAG" then title (30) and artist (30).
        private static string Id3v1(string path)
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 128) return null;
            stream.Seek(-128, SeekOrigin.End);
            var tag = new byte[128];
            if (stream.Read(tag, 0, 128) < 128 || tag[0] != 'T' || tag[1] != 'A' || tag[2] != 'G')
                return null;
            string artist = Latin1(tag, 33, 30).Trim('\0', ' ');
            return artist.Length > 0 ? artist : null;
        }

        private static int Syncsafe(byte[] data, int at)
            => (data[at] & 0x7F) << 21 | (data[at + 1] & 0x7F) << 14 | (data[at + 2] & 0x7F) << 7 | (data[at + 3] & 0x7F);

        private static int BigEndian(byte[] data, int at)
            => data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3];

        #endregion

        #region Vorbis comments (OGG)

        private static string Vorbis(byte[] data)
        {
            // The comment header: packet type 3, then "vorbis" (it starts in the second Ogg page).
            byte[] marker = { 3, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
            int at = IndexOf(data, marker, 0);
            if (at < 0) return null;
            int pos = at + marker.Length;
            if (pos + 4 > data.Length) return null;
            int vendorLength = LittleEndian(data, pos);
            pos += 4 + vendorLength;
            if (pos + 4 > data.Length || vendorLength < 0) return null;
            int count = LittleEndian(data, pos);
            pos += 4;
            string artist = null, composer = null;
            for (int i = 0; i < count && pos + 4 <= data.Length; i++)
            {
                int length = LittleEndian(data, pos);
                pos += 4;
                if (length < 0 || pos + length > data.Length) break;
                string comment = Encoding.UTF8.GetString(data, pos, length);
                pos += length;
                int equals = comment.IndexOf('=');
                if (equals <= 0) continue;
                string key = comment.Substring(0, equals).ToUpperInvariant();
                string value = comment.Substring(equals + 1).Trim();
                if (value.Length == 0) continue;
                if (key == "ARTIST") artist ??= value;
                else if (key == "COMPOSER") composer ??= value;
            }
            return artist ?? composer;
        }

        #endregion

        #region RIFF INFO (WAV)

        private static string RiffInfo(byte[] data)
        {
            if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
                return null;
            int pos = 12;
            while (pos + 8 <= data.Length)
            {
                string id = Encoding.ASCII.GetString(data, pos, 4);
                int size = LittleEndian(data, pos + 4);
                if (size < 0) break;
                if (id == "LIST" && pos + 12 <= data.Length && Encoding.ASCII.GetString(data, pos + 8, 4) == "INFO")
                {
                    int sub = pos + 12, end = Math.Min(data.Length, pos + 8 + size);
                    while (sub + 8 <= end)
                    {
                        string subId = Encoding.ASCII.GetString(data, sub, 4);
                        int subSize = LittleEndian(data, sub + 4);
                        if (subSize < 0 || sub + 8 + subSize > end) break;
                        if (subId == "IART")
                            return Encoding.UTF8.GetString(data, sub + 8, subSize).Trim('\0', ' ');
                        sub += 8 + subSize + (subSize & 1);
                    }
                }
                pos += 8 + size + (size & 1);
            }
            return null;
        }

        #endregion

        // ISO-8859-1: each byte is the character with that code.
        private static string Latin1(byte[] data, int offset, int count)
        {
            var chars = new char[count];
            for (int i = 0; i < count; i++)
                chars[i] = (char)data[offset + i];
            return new string(chars);
        }

        private static int LittleEndian(byte[] data, int at)
            => data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24;

        private static int IndexOf(byte[] data, byte[] pattern, int from)
        {
            for (int i = from; i <= data.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }
    }
}
