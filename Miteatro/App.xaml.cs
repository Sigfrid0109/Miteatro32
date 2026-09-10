using System;
using System.IO;
using System.Threading;
using System.Windows;
namespace Miteatro
{
    public partial class App : Application
    {
        private Mutex instance;
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length == 1 && e.Args[0] == "--backup")
            {
                try
                {
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiTeatro");
                    var config = File.Exists(Path.Combine(folder, "connection.xml")) ? Codec.Decode<ConnectionSettings>(File.ReadAllText(Path.Combine(folder, "connection.xml"))) : new ConnectionSettings();
                    IRepository repository = config.MySql ? (IRepository)new MySqlRepository(config.ConnectionString()) : new LocalRepository(Path.Combine(folder, "theater.xml"));
                    var data = repository.Load();
                    TheaterService.Validate(data);
                    TheaterService.Require(!string.IsNullOrWhiteSpace(data.Settings.BackupFolder), "Configura una carpeta de respaldos.");
                    Codec.AtomicWrite(Path.Combine(data.Settings.BackupFolder, "MiTeatro-" + DateTime.Today.ToString("yyyy-MM-dd") + ".xml"), Codec.Encode(data));
                    Shutdown(0);
                }
                catch (Exception ex) { Log(ex); Shutdown(1); }
                return;
            }
            bool created;
            instance = new Mutex(true, "Local\\MiTeatro.SingleCashRegister", out created);
            if (!created)
            {
                MessageBox.Show("Mi Teatro ya está abierto. Usa la ventana existente para operar la caja.", "Mi Teatro");
                Shutdown();
                return;
            }
            DispatcherUnhandledException += (sender, args) =>
            {
                Log(args.Exception);
                MessageBox.Show("Ocurrió un error inesperado. La aplicación se cerrará para proteger las operaciones.\nConsulta el registro errors.log en la carpeta de datos de Mi Teatro.", "Mi Teatro");
                args.Handled = true;
                Shutdown(1);
            };
            base.OnStartup(e);
        }
        static void Log(Exception ex)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiTeatro");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "errors.log"), DateTime.Now + " " + ex + Environment.NewLine);
            }
            catch { }
        }
        protected override void OnExit(ExitEventArgs e)
        {
            instance?.Dispose();
            base.OnExit(e);
        }
    }
}
