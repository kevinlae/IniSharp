using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IniFileSharp
{
    public class IniSharp
    {
        private string filePath;

        private char commentChar = '#';

        private static object lockObject = new object();

        public Encoding FileEncoding { get; private set; }

        public IniSharp(string filePath)
        {
            this.filePath = Path.GetFullPath(filePath);
            if (!File.Exists(this.filePath))
            {
                File.Create(this.filePath).Close();
            }
            FileEncoding = GetFileEncoding();
        }
        public IniSharp(string filePath, char commentChar) : this(filePath)
        {
            this.commentChar = commentChar;
        }
        public IniSharp(string filePath, Encoding fileEncoding) : this(filePath)
        {
            FileEncoding = fileEncoding;
        }
        public IniSharp(string filePath, char commentChar, Encoding fileEncoding) : this(filePath)
        {
            this.commentChar = commentChar;
            FileEncoding = fileEncoding;
        }

 
        private string GetPrivateProfileString(string section, string key, string defaultValue = null)
        {
            if (string.IsNullOrWhiteSpace(section))
            {
                throw new ArgumentException("Section cannot be null or whitespace.", "section");
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key cannot be null or whitespace.", "key");
            }

            List<string> lines = File.ReadAllLines(filePath, FileEncoding).ToList();
            (int sectionNum, int keyNum) = FindSectionAndKey(section, key, lines);
            if (sectionNum != -1 && keyNum != -1)
            {
                int startIndex = lines[keyNum].IndexOf(key);
                int equalsIndex = lines[keyNum].IndexOf('=', startIndex + key.Length);
                string strLalue = lines[keyNum].Substring(equalsIndex + 1);
                int hashIndex = strLalue.IndexOf(commentChar);
                return (hashIndex != -1) ? strLalue.Substring(0, hashIndex) : strLalue;
            }
            if (defaultValue != null)
            {
                if (sectionNum != -1)
                {
                    if (keyNum == -1)
                    {
                        lines.Insert(sectionNum + 1, $"{key}={defaultValue}");
                        lock (lockObject)
                        {
                            File.WriteAllLines(filePath, lines, FileEncoding);
                        }
                    }
                }
                else
                {
                    lock (lockObject)
                    {
                        using (StreamWriter sw = File.AppendText(filePath))
                        {
                            sw.WriteLine($"[{section}]");
                            sw.WriteLine($"{key}={defaultValue}");
                        }
                    }
                }
            }
            return defaultValue;
        }

 
        private bool WritePrivateProfileString(string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(section))
            {
                throw new ArgumentException("Section cannot be null or whitespace.", "section");
            }

            List<string> lines = File.ReadAllLines(filePath, FileEncoding).ToList();
            (int sectionNum, int keyNum) = FindSectionAndKey(section, key, lines);
            if (sectionNum == -1)
            {
                if (value is null || key is null)
                {
                    return false;
                }
                lock (lockObject)
                {
                    using (StreamWriter sw = File.AppendText(filePath))
                    {
                        sw.WriteLine($"[{section}]");
                        sw.WriteLine($"{key}={value}");
                    }
                }
            }
            else
            {
                if (keyNum == -1)
                {
                    if (key is null)
                    {
                        int endIndex = lines.FindIndex(sectionNum + 1, line => line.TrimStart().StartsWith("[") && line.Contains(']'));
                        if (endIndex == -1)
                        {
                            for (int i = sectionNum + 1; i < lines.Count; i++)
                            {
                                if (lines[i].Contains('='))
                                {
                                    endIndex = i;
                                }
                            }
                            endIndex++;
                        }
                        lines.RemoveRange(sectionNum, endIndex - sectionNum);

                    }
                    else if (value is null)
                    {
                        return false;
                    }
                    else
                    {
                        lines.Insert(sectionNum + 1, $"{key}={value}");
                    }
                }
                else
                {
                    if (value is null)
                    {
                        lines.RemoveAt(keyNum);
                        if (lines[sectionNum + 1].TrimStart().StartsWith("[") && lines[sectionNum + 1].Contains(']'))
                        {
                            lines.RemoveAt(sectionNum);
                        }
                    }
                    else
                    {
                        int startIndex = lines[keyNum].IndexOf(key);
                        int equalsIndex = lines[keyNum].IndexOf('=', startIndex + key.Length);
                        string strKey = lines[keyNum].Substring(0, equalsIndex);
                        string strValue = lines[keyNum].Substring(equalsIndex + 1);

                        int hashIndex = strValue.IndexOf(commentChar);
                        if (hashIndex != -1)
                        {
                            int commentIndex = strValue.IndexOf(commentChar);
                            lines[keyNum] = $" {strKey}={value}{strValue.Substring(commentIndex)}";
                        }
                        else
                        {
                            lines[keyNum] = $"{strKey}={value}";
                        }
                    }
                }
                lock (lockObject)
                {
                    File.WriteAllLines(filePath, lines, FileEncoding);
                }
            }
            return true;
        }



        private (int sectionNum, int keyNum) FindSectionAndKey(string section, string key, List<string> lines)
        {
            int sectionNum = -1;
            int keyNum = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("[") && lines[i].Contains(']'))
                {
                    int endIndex = line.IndexOf(']');
                    int startIndex = line.IndexOf('[');
                    string lineSection = line.Substring(startIndex + 1, endIndex - 1);
                    if (string.Equals(lineSection, section, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionNum = i;
                        continue;
                    }
                    else if (sectionNum != -1)
                    {
                        break;
                    }
                }
                else if (sectionNum != -1 && line.StartsWith(key, StringComparison.OrdinalIgnoreCase) && lines[i].Contains("="))
                {
                    int equalsIndex = line.IndexOf('=', key.Length);
                    string betweenKeyAndEquals = line.Substring(key.Length, equalsIndex - key.Length);
                    if (string.IsNullOrWhiteSpace(betweenKeyAndEquals))
                    {
                        keyNum = i;
                        break;
                    }
                }
            }
            return (sectionNum, keyNum);
        }


        public string GetValue(string section, string key, string defaultValue = null)
        { 
            return GetPrivateProfileString(section, key, defaultValue) ?? defaultValue;
        }

        public bool SetValue(string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key cannot be null or whitespace.", "key");
            }
            if (value is null)
            {
                throw new ArgumentException("Value cannot be null.", "Value");
            }
            return WritePrivateProfileString(section, key, value);
        }

        public bool DeleteKey(string section, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key cannot be null or whitespace.", "key");
            }
            return WritePrivateProfileString(section, key, null);
        }

        public bool DeleteSection(string section)
        {
            return WritePrivateProfileString(section, null, null);
        }

        public bool DeleteAllSection()
        {
            try
            {
                List<string> sectionList = GetSections();
                foreach (string section in sectionList)
                {
                    DeleteSection(section);
                }
                return true;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public void DeleteInvalidLines()
        {
            try
            {
                List<string> lines = File.ReadLines(filePath, FileEncoding).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!lines[i].Contains('[') && !lines[i].Contains(']') && !lines[i].Contains('=') && !lines[i].Contains(commentChar))
                    {
                        lines.RemoveAt(i);
                        i--;
                    }
                }
                lock (lockObject)
                {
                    File.WriteAllLines(filePath, lines, FileEncoding);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public List<string> GetKeys(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
            {
                throw new ArgumentException("Section cannot be null or whitespace.", "section");
            }
            List<string> keys = new List<string>();

            List<string> lines = File.ReadAllLines(filePath, FileEncoding).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].TrimStart();
                if (!line.StartsWith("[") || !line.Contains(']'))
                {
                    continue;
                }
                //int startIndex = line.IndexOf('[') + 1;
                int endIndex = line.IndexOf(']');
                string lineSection = line.Substring(1, endIndex - 1).Trim();
                if (!string.Equals(lineSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                for (int j = i + 1; j < lines.Count; j++)
                {
                    line = lines[j].TrimStart();
                    if (line.StartsWith("[") && line.Contains(']'))
                    {
                        break;
                    }
                    int equalIndex = line.IndexOf('=');
                    if (equalIndex > 0)
                    {
                        string lineKey = line.Substring(0, equalIndex).Trim();
                        keys.Add(lineKey);
                    }
                }
                break;
            }
            return keys;
        }

        public List<string> GetSections()
        {
            List<string> lines = File.ReadAllLines(filePath, FileEncoding).ToList();
            List<string> sections = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("[") && line.Contains(']'))
                {
                    //int startIndex = line.IndexOf('[') + 1;
                    int endIndex = line.IndexOf(']');
                    string lineSection = line.Substring(1, endIndex - 1).Trim();
                    if (!string.IsNullOrWhiteSpace(lineSection))
                    {
                        sections.Add(lineSection);
                    }
                }
            }
            return sections;
        }



        private Encoding GetFileEncoding()
        {
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead = fileStream.Read(buffer, 0, 4096);
                    if (bytesRead >= 4)
                    {
                        if (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 254 && buffer[3] == byte.MaxValue)
                        {
                            return Encoding.GetEncoding("utf-32BE");
                        }
                        if (buffer[0] == byte.MaxValue && buffer[1] == 254 && buffer[2] == 0 && buffer[3] == 0)
                        {
                            return Encoding.UTF32;
                        }
                    }
                    if (bytesRead >= 2)
                    {
                        if (buffer[0] == byte.MaxValue && buffer[1] == 254)
                        {
                            return Encoding.Unicode;
                        }
                        if (buffer[0] == 254 && buffer[1] == byte.MaxValue)
                        {
                            return Encoding.BigEndianUnicode;
                        }
                    }
                    if (bytesRead >= 3 && buffer[0] == 239 && buffer[1] == 187 && buffer[2] == 191)
                    {
                        return Encoding.UTF8;
                    }
                    if (IsUtf8Bytes(buffer, bytesRead))
                    {
                        return Encoding.UTF8;
                    }
                    return Encoding.Default;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error detecting encoding for file '" + filePath + "': " + ex.Message);
                return Encoding.Default;
            }
        }

        private bool IsUtf8Bytes(byte[] bytes, int bytesLength)
        {
            int i = 0;
            while (i < bytesLength)
            {
                byte currentByte = bytes[i++];
                if (currentByte < 128)
                {
                    continue;
                }
                int bytesCount = 0;
                if ((currentByte & 0xE0) == 192)
                {
                    bytesCount = 1;
                }
                else if ((currentByte & 0xF0) == 224)
                {
                    bytesCount = 2;
                }
                else if ((currentByte & 0xF8) == 240)
                {
                    bytesCount = 3;
                }
                else
                {
                    if ((currentByte & 0xFC) != 248)
                    {
                        return false;
                    }
                    bytesCount = 4;
                }
                for (int j = 0; j < bytesCount; j++)
                {
                    if (i >= bytesLength || (bytes[i++] & 0xC0) != 128)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
