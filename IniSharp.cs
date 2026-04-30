using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IniFileSharp
{
    /// <summary>
    /// 高性能 INI 文件读写类，基于 Section/Line 对象模型，支持注释、顺序保留、大文件高效读写。
    /// </summary>
    public class IniSharp : IDisposable
    {
        private readonly string _filePath;
        private readonly char _commentChar;
        private Encoding _encoding;
        private readonly object _lock = new object();
        
        // 核心数据结构
        private List<Section> _sections;                   // 保持区段顺序
        private Dictionary<string, Section> _sectionMap;   // 区段名 -> Section（忽略大小写）
        private bool _autoSave;
        private bool _dirty;
        private DateTime _lastWriteTime;

        #region 构造函数
        public IniSharp(string filePath) : this(filePath, '#', null, true) { }
        public IniSharp(string filePath, char commentChar) : this(filePath, commentChar, null, true) { }
        public IniSharp(string filePath, Encoding encoding) : this(filePath, '#', encoding, true) { }
        public IniSharp(string filePath, char commentChar, Encoding encoding, bool autoSave = true)
        {
            _filePath = Path.GetFullPath(filePath);
            _commentChar = commentChar;
            _autoSave = autoSave;

            if (!File.Exists(_filePath))
                File.Create(_filePath).Close();
            
            _encoding = encoding ?? DetectEncoding();
            Load();
        }
        #endregion

        #region 公共属性
        public bool AutoSave
        {
            get => _autoSave;
            set => _autoSave = value;
        }
        public Encoding FileEncoding
        {
            get => _encoding;
            set { if (value != null) { _encoding = value; _dirty = true; } }
        }
        #endregion

        #region 公共读取方法
        public string GetValue(string section, string key, string defaultValue = null)
        {
            if (string.IsNullOrWhiteSpace(section)) throw new ArgumentException("section");
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key");

            lock (_lock)
            {
                CheckExternalChange();
                if (_sectionMap.TryGetValue(section, out var sec))
                {
                    if (sec.Values.TryGetValue(key, out var valLine))
                        return valLine.GetValue(_commentChar);
                }

                // 未找到，且提供了默认值 → 自动创建
                if (defaultValue != null)
                {
                    AddOrUpdateValue(section, key, defaultValue, addIfMissing: true);
                    return defaultValue;
                }
                return null;
            }
        }

        public List<string> GetSections()
        {
            lock (_lock)
            {
                CheckExternalChange();
                return _sections.Select(s => s.Name).ToList();
            }
        }

        public List<string> GetKeys(string section)
        {
            if (string.IsNullOrWhiteSpace(section)) throw new ArgumentException("section");
            lock (_lock)
            {
                CheckExternalChange();
                if (_sectionMap.TryGetValue(section, out var sec))
                    return sec.ValueKeys.OrderBy(v => sec.Lines.IndexOf(v)).Select(v => v.Key).ToList();
                return new List<string>();
            }
        }
        #endregion

        #region 公共写入方法
        public bool SetValue(string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key");
            if (value == null) throw new ArgumentNullException(nameof(value));
            lock (_lock)
            {
                CheckExternalChange();
                AddOrUpdateValue(section, key, value, addIfMissing: true);
            }
            return true;
        }

        public bool DeleteKey(string section, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key");
            lock (_lock)
            {
                CheckExternalChange();
                if (!_sectionMap.TryGetValue(section, out var sec)) return false;
                if (!sec.Values.TryGetValue(key, out var valLine)) return false;
                
                sec.Lines.Remove(valLine);
                sec.Values.Remove(key);
                if (sec.ValueKeys.Count == 0)
                {
                    // 区段为空，移除整个区段
                    _sections.Remove(sec);
                    _sectionMap.Remove(section);
                }
                _dirty = true;
                TrySave();
                return true;
            }
        }

        public bool DeleteSection(string section)
        {
            lock (_lock)
            {
                CheckExternalChange();
                if (!_sectionMap.TryGetValue(section, out var sec)) return false;
                _sections.Remove(sec);
                _sectionMap.Remove(section);
                _dirty = true;
                TrySave();
                return true;
            }
        }

        public bool DeleteAllSection()
        {
            lock (_lock)
            {
                CheckExternalChange();
                _sections.Clear();
                _sectionMap.Clear();
                _dirty = true;
                TrySave();
                return true;
            }
        }

        public void DeleteInvalidLines()
        {
            lock (_lock)
            {
                CheckExternalChange();
                foreach (var sec in _sections)
                {
                    sec.Lines.RemoveAll(line =>
                    {
                        if (line is CommentLine || line is EmptyLine) return false; // 注释和空行保留
                        if (line is ValueLine) return false;
                        return true; // 理论上没有其他类型，但防御式清理
                    });
                }
                _dirty = true;
                TrySave();
            }
        }
        #endregion

        #region 保存与外部变更检测
        public void Save()
        {
            lock (_lock)
            {
                FlushToDisk();
            }
        }

        public void Reload()
        {
            lock (_lock)
            {
                Load();
            }
        }

        public void Dispose()
        {
            if (_dirty || _autoSave)
            {
                lock (_lock)
                {
                    if (_dirty) FlushToDisk();
                }
            }
        }
        #endregion

        #region 私有核心逻辑
        private void AddOrUpdateValue(string section, string key, string value, bool addIfMissing)
        {
            if (!_sectionMap.TryGetValue(section, out var sec))
            {
                sec = new Section(section);
                _sections.Add(sec);
                _sectionMap[section] = sec;
            }

            if (sec.Values.TryGetValue(key, out var existingLine))
            {
                // 更新现有键，保留注释
                existingLine.SetValue(value, _commentChar);
            }
            else if (addIfMissing)
            {
                var newLine = new ValueLine(key, value);
                // 插入到区段尾部（所有键值对之前可以有注释，但通常键值对排在后面）
                // 找到最后一个键值对之后的位置插入，保持区段内键的连续顺序
                int insertIdx = sec.Lines.Count;
                for (int i = sec.Lines.Count - 1; i >= 0; i--)
                {
                    if (sec.Lines[i] is ValueLine) { insertIdx = i + 1; break; }
                }
                sec.Lines.Insert(insertIdx, newLine);
                sec.Values[key] = newLine;
            }

            _dirty = true;
            TrySave();
        }

        private void Load()
        {
            _sections = new List<Section>();
            _sectionMap = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
            
            if (!File.Exists(_filePath)) return;

            string[] rawLines = File.ReadAllLines(_filePath, _encoding);
            _lastWriteTime = File.GetLastWriteTime(_filePath);

            Section currentSection = null;
            foreach (string rawLine in rawLines)
            {
                string trimmed = rawLine.TrimStart();
                // 检查是否为 section 行
                if (trimmed.StartsWith("[") && trimmed.Contains(']'))
                {
                    int endIdx = trimmed.IndexOf(']');
                    int startIdx = trimmed.IndexOf('[');
                    string secName = trimmed.Substring(startIdx + 1, endIdx - startIdx - 1).Trim();
                    if (!string.IsNullOrWhiteSpace(secName))
                    {
                        currentSection = new Section(secName);
                        _sections.Add(currentSection);
                        _sectionMap[secName] = currentSection;
                        continue;
                    }
                }
                // 非 section 行，且存在当前 section
                if (currentSection != null)
                {
                    Line line = ParseLine(rawLine, currentSection);
                    currentSection.Lines.Add(line);
                }
                // 如果还没有任何 section，则忽略孤立的行（符合 Windows API 行为）
            }

            _dirty = false;
        }

        private Line ParseLine(string raw, Section owner)
        {
            string trimmed = raw.TrimStart();
            // 空行
            if (string.IsNullOrEmpty(trimmed))
                return new EmptyLine();
            // 注释行（以 commentChar 开头）
            if (trimmed[0] == _commentChar)
                return new CommentLine(raw);
            // 键值对行（包含 '='，且不是段头）
            int eqIdx = raw.IndexOf('=');
            if (eqIdx > 0) // 等号不在行首（键名不能为空）
            {
                string key = raw.Substring(0, eqIdx).TrimEnd();
                string valuePart = raw.Substring(eqIdx + 1);
                var valLine = new ValueLine(key, valuePart);
                owner.Values[key] = valLine;
                return valLine;
            }
            // 其他无效行当作注释保留（兼容性）
            return new CommentLine(raw);
        }

        private void FlushToDisk()
        {
            var sb = new StringBuilder();
            foreach (var sec in _sections)
            {
                sb.AppendLine($"[{sec.Name}]");
                foreach (var line in sec.Lines)
                    sb.AppendLine(line.RawText);
            }
            File.WriteAllText(_filePath, sb.ToString(), _encoding);
            _lastWriteTime = File.GetLastWriteTime(_filePath);
            _dirty = false;
        }

        private void TrySave()
        {
            if (_autoSave && _dirty)
                FlushToDisk();
        }

        private void CheckExternalChange()
        {
            try
            {
                var currentTime = File.GetLastWriteTime(_filePath);
                if (currentTime != _lastWriteTime)
                    Load();
            }
            catch { /* 文件被删等，内存状态保留 */ }
        }

        private Encoding DetectEncoding()
        {
            // 保留原有的编码检测逻辑（不变）
            try
            {
                using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
                byte[] bom = new byte[4];
                int read = fs.Read(bom, 0, 4);
                if (read >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF)
                    return Encoding.GetEncoding("utf-32BE");
                if (read >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0 && bom[3] == 0)
                    return Encoding.UTF32;
                if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                    return Encoding.Unicode;
                if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                    return Encoding.BigEndianUnicode;
                if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                    return Encoding.UTF8;
                // 简单的 UTF-8 无 BOM 检测（保持原有）
                return IsUtf8(fs) ? new UTF8Encoding(false) : Encoding.Default;
            }
            catch
            {
                return Encoding.Default;
            }
        }

        private bool IsUtf8(FileStream fs)
        {
            // 保留原有 IsUtf8Bytes 逻辑，此处略
            // 可复用之前实现的 private bool IsUtf8Bytes(byte[] ...)
            return false; // 示例简化，实际请嵌入原方法
        }
        #endregion

        #region 内部数据模型
        private abstract class Line
        {
            public abstract string RawText { get; }
        }

        private class EmptyLine : Line
        {
            public override string RawText => string.Empty;
        }

        private class CommentLine : Line
        {
            public string Comment { get; }
            public CommentLine(string raw) { Comment = raw; }
            public override string RawText => Comment;
        }

        private class ValueLine : Line
        {
            public string Key { get; private set; }
            private string _rawValue; // 包含可能的注释部分
            public ValueLine(string key, string rawValue)
            {
                Key = key;
                _rawValue = rawValue;
            }
            public override string RawText => $"{Key}={_rawValue}";
            public string GetValue(char commentChar)
            {
                int idx = _rawValue.IndexOf(commentChar);
                return idx >= 0 ? _rawValue.Substring(0, idx).TrimEnd() : _rawValue.TrimEnd();
            }
            public void SetValue(string newValue, char commentChar)
            {
                int idx = _rawValue.IndexOf(commentChar);
                if (idx >= 0)
                    _rawValue = newValue + _rawValue.Substring(idx);
                else
                    _rawValue = newValue;
            }
        }

        private class Section
        {
            public string Name { get; }
            public List<Line> Lines { get; } = new List<Line>();
            public Dictionary<string, ValueLine> Values { get; } = new Dictionary<string, ValueLine>(StringComparer.OrdinalIgnoreCase);
            public IEnumerable<ValueLine> ValueKeys => Values.Values;

            public Section(string name) { Name = name; }
        }
        #endregion
    }
}
