using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Bfocus.Widget
{
    /// <summary>
    /// Persistência de estado pequeno do widget (<c>last_seen</c>, token de push). Implemente para
    /// guardar em outro lugar (registro, banco do app, armazenamento seguro).
    /// </summary>
    public interface IStateStore
    {
        string? Get(string key);

        /// <summary>Grava; <c>null</c> apaga a chave.</summary>
        void Set(string key, string? value);
    }

    /// <summary>Em memória (testes, ou quando o app não quer nada em disco).</summary>
    public sealed class InMemoryStateStore : IStateStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public string? Get(string key)
        {
            lock (_lock) return _values.TryGetValue(key, out var v) ? v : null;
        }

        public void Set(string key, string? value)
        {
            lock (_lock)
            {
                if (value == null) _values.Remove(key);
                else _values[key] = value;
            }
        }
    }

    /// <summary>
    /// Arquivo JSON em <c>%LOCALAPPDATA%\bfocus\widget-state.json</c> (Windows) ou o equivalente
    /// (<c>~/.local/share/bfocus</c>, <c>~/Library/…</c>). Falha de disco nunca derruba o widget: o
    /// valor continua em memória.
    /// </summary>
    public sealed class FileStateStore : IStateStore
    {
        private readonly object _lock = new object();
        private Dictionary<string, string>? _cache;

        public FileStateStore(string? filePath = null)
        {
            FilePath = filePath ?? DefaultFilePath();
        }

        public string FilePath { get; }

        public static string DefaultDirectory()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(baseDir, "bfocus");
        }

        public static string DefaultFilePath() => Path.Combine(DefaultDirectory(), "widget-state.json");

        public string? Get(string key)
        {
            lock (_lock)
            {
                return Load().TryGetValue(key, out var v) ? v : null;
            }
        }

        public void Set(string key, string? value)
        {
            lock (_lock)
            {
                var data = Load();
                if (value == null)
                {
                    if (!data.Remove(key)) return;
                }
                else
                {
                    if (data.TryGetValue(key, out var old) && old == value) return;
                    data[key] = value;
                }
                Save(data);
            }
        }

        private Dictionary<string, string> Load()
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(FilePath))
                {
                    // JsonDocument em vez do JsonSerializer: seguro para trimming/AOT.
                    using (var doc = JsonDocument.Parse(File.ReadAllText(FilePath)))
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            foreach (var p in doc.RootElement.EnumerateObject())
                                if (p.Value.ValueKind == JsonValueKind.String) _cache[p.Name] = p.Value.GetString()!;
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is NotSupportedException)
            {
                // Arquivo corrompido ou sem permissão: recomeça do zero (pior caso, 1ª visita de novo).
            }
            return _cache;
        }

        private void Save(Dictionary<string, string> data)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = FilePath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    foreach (var kv in data) w.WriteString(kv.Key, kv.Value);
                    w.WriteEndObject();
                }
                // Troca atômica: um app fechado no meio da gravação não deixa JSON pela metade.
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException)
            {
                // Mantém em memória; tenta gravar de novo na próxima mudança.
            }
        }
    }
}
