using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IniSharp
{
    public class IniSharp
    {
        private string filePath;

        private Encoding encoding;

        private const char commentChar = '#';

        private static object lockObject = new object();

        public IniSharp(string filePath)
        {
            this.filePath = Path.GetFullPath(filePath);
            if (!File.Exists(this.filePath))
            {
                File.Create(this.filePath).Close();
            }
            encoding = GetFileEncoding(this.filePath);
        }
        /// <summary>
        /// Retrieves a string from the specified section of an ini file.
        /// </summary>
        /// <param name="section">The name of the section containing the key.</param>
        /// <param name="key">The name of the key.</param>
        /// <param name="defaultValue">The default value to return if the key is not found.</param>
        /// <returns>
        /// If the function succeeds, the return value is the retrieved string.
        /// If the function fails, the return value is the default value specified by defaultValue.
        /// </returns>
        /// <exception cref="ArgumentException"></exception>
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

            List<string> lines = File.ReadAllLines(filePath, encoding).ToList();
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
                            File.WriteAllLines(filePath, lines, encoding);
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

        /// <summary>
        /// Writes a string to a specific key within a section in an INI file.
        /// </summary>
        /// <param name="section">The name of the section in the INI file.</param>
        /// <param name="key">The name of the key within the specified section.</param>
        /// <param name="value">The string value to be written to the key.</param>
        /// <returns><c>true</c> if the operation is successful; otherwise, <c>false</c>.</returns>
        ///
        /// <exception cref="ArgumentException">Thrown when one or more of the input arguments is invalid.</exception>
        ///
        /// <remarks>
        /// This method writes the specified string value to the specified key within the specified section in an INI file.
        /// If the section or the key does not exist, they will be created. If the key already has a value, it will be overwritten.
        /// The operation may fail if the INI file is read-only or if there are insufficient permissions to write to the file.
        /// </remarks>
        private bool WritePrivateProfileString(string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(section))
            {
                throw new ArgumentException("Section cannot be null or whitespace.", "section");
            }

            List<string> lines = File.ReadAllLines(filePath, encoding).ToList();
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
                    File.WriteAllLines(filePath, lines, encoding);
                }
            }
            return true;
        }

        /// <summary>
        /// Finds the section and key indices within a list of lines representing an INI file.
        /// </summary> 

        /// <param name="section">The name of the section to search for.</param>
        /// <param name="key">The name of the key to search for.</param>
        /// <param name="lines">A list of strings representing the lines of the INI file.</param>
        /// <returns>A <see cref="ValueTuple{T1,T2}"/> containing the indices of the section and key respectively,
        /// or (-1, -1) if the section or key is not found.</returns>
        ///
        /// <exception cref="ArgumentNullException">Thrown when the section, key, or lines parameter is null.</exception>
        ///
        /// <remarks>
        /// This method searches for the specified section and key within the list of lines representing an INI file.
        /// It returns a tuple containing the indices of the section and key if found, or (-1, -1) if either the section or key is not found.
        /// The search is case-sensitive, and the section and key names must match exactly.
        /// </remarks>

        private (int sectionNum, int keyNum) FindSectionAndKey(string section, string key, List<string> lines)
        {
            int sectionNum = -1;
            int keyNum = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("[") && line.Contains(']'))
                {
                    int endIndex = line.IndexOf(']');
                    string lineSection = line.Substring(1, endIndex - 1).Trim();
                    if (string.Equals(lineSection, section, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionNum = i;
                        if (key is null)
                        {
                            break;
                        }
                    }
                    else if (sectionNum != -1)
                    {
                        break;
                    }
                }
                else if (sectionNum != -1 && line.StartsWith(key, StringComparison.OrdinalIgnoreCase) && line.Contains("="))
                {
                    int equalsIndex = line.IndexOf('=', key.Length);
                    string betweenKeyAndEquals = line.Substring(key.Length, equalsIndex - key.Length).Trim();
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
            string result = GetPrivateProfileString(section, key, defaultValue);
            if (result == null)
            {
                throw new ArgumentNullException();
            }
            return result;
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
                List<string> lines = File.ReadLines(filePath, encoding).ToList();
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
                    File.WriteAllLines(filePath, lines, encoding);
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

            List<string> lines = File.ReadAllLines(filePath, encoding).ToList();
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
            List<string> lines = File.ReadAllLines(filePath, encoding).ToList();
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



        public Encoding GetFileEncoding(string filePath)
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
