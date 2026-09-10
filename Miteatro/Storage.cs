using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Xml.Serialization;
using MySql.Data.MySqlClient;
namespace Miteatro
{
    public sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
    public static class Codec
    {
        public static string Encode<T>(T value)
        {
            using (var w = new Utf8StringWriter())
            {
                new XmlSerializer(typeof(T)).Serialize(w, value);
                return w.ToString();
            }
        }
        public static T Decode<T>(string value)
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100000000 };
            using (var r = System.Xml.XmlReader.Create(new StringReader(value), settings))
                return (T)new XmlSerializer(typeof(T)).Deserialize(r);
        }
        public static T Clone<T>(T value) => Decode<T>(Encode(value));
        public static void AtomicWrite(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, Encoding.UTF8);
            if (File.Exists(path))
                File.Replace(temp, path, path + ".bak");
            else
                File.Move(temp, path);
        }
    }
    public interface IRepository
    {
        Database Load(); void Save(Database db, long expected); string Description
        {
            get;
        }
    }
    public class LocalRepository : IRepository
    {
        readonly string path; public LocalRepository(string path)
        {
            this.path = path;
        }
        public string Description => "Archivo local";
        public Database Load() => File.Exists(path) ? Codec.Decode<Database>(File.ReadAllText(path)) : new Database();
        public void Save(Database db, long expected)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var gate = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var current = Load();
                if (current.Revision != expected)
                    throw new InvalidOperationException("Los datos cambiaron en otra instancia. Reinicia la aplicación.");
                Codec.AtomicWrite(path, Codec.Encode(db));
            }
        }
    }
    public class ConnectionSettings
    {
        public bool MySql
        {
            get; set;
        }
        public string Server { get; set; } = "localhost"; public uint Port { get; set; } = 3306; public string Database { get; set; } = "miteatro"; public string User { get; set; } = "miteatro"; public string Secret { get; set; } = ""; public bool Tls { get; set; } = true;
        public string ConnectionString()
        {
            var password = string.IsNullOrEmpty(Secret) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Secret), null, DataProtectionScope.CurrentUser));
            return new MySqlConnectionStringBuilder { Server = Server, Port = Port, Database = Database, UserID = User, Password = password, SslMode = Tls ? MySqlSslMode.Required : MySqlSslMode.Disabled, ConnectionTimeout = 5, DefaultCommandTimeout = 15 }.ConnectionString;
        }
        public void SetPassword(string password)
        {
            Secret = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
        }
    }
    public static class Passwords
    {
        const int Iterations = 210000;
        public static string Hash(string password)
        {
            if (password == null || password.Length < 10)
                throw new InvalidOperationException("Usa una contraseña de al menos 10 caracteres.");
            var salt = new byte[24];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return Iterations + ":" + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(kdf.GetBytes(32));
        }
        public static bool Verify(string password, string hash)
        {
            try
            {
                var p = hash.Split(':');
                using (var kdf = new Rfc2898DeriveBytes(password, Convert.FromBase64String(p[1]), int.Parse(p[0]), HashAlgorithmName.SHA256))
                {
                    var a = kdf.GetBytes(32);
                    var b = Convert.FromBase64String(p[2]);
                    int diff = a.Length ^ b.Length;
                    for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                        diff |= a[i] ^ b[i];
                    return diff == 0;
                }
            }
            catch { return false; }
        }
    }
}


