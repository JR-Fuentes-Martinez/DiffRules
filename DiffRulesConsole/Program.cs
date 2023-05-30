using DiffRulesLib;
using FluentFTP;
using System.Diagnostics;
using System.Data.SQLite;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.CommandLine;
using System.IO;

int UsandoRec = 0;

try {
    var localRootOpt = new Option<string>(name: "--localroot", description: "Ruta de directorio local para el mirror FTP (required)");
    var rutaScriptOpt = new Option<string>(name: "--rutascript", description: "Ruta del script plantila de NCL y archivos tempòrales (required)");
    var rutaDbOpt = new Option<string>(name: "--rutadb", description: "Ruta de la base de datos de metadata (required)");
    var FInicioOpt = new Option<DateOnly>(name: "--fechaInicio", description: "Fecha de inicio para la recueración en batch. (required)");
    var FFinOpt = new Option<DateOnly>(name: "--fechafin", description: "Fecha de fin para la recuperación en batch.)",
        getDefaultValue: () => DateOnly.FromDateTime(DateTime.Now));
    var TCicloOpt = new Option<int>(name: "--tciclo", description: "Tiempo en minutos entre conexiones FTP.",
        getDefaultValue: () => 5);
    var RedStdOutOpt = new Option<bool>(name: "--redstdout", description: "Redirige la StdOut a un archivo local <StdOut.txt>.",
        getDefaultValue: () => false);

    var rootCommand = new RootCommand("Entrada de datos UI");
    var readCommand = new Command("read", "Lee los datos necesarios de entrada UI") {
        localRootOpt,
        rutaScriptOpt,
        rutaDbOpt,
        FInicioOpt,
        FFinOpt,
        TCicloOpt,
        RedStdOutOpt
    };    
    rootCommand.AddCommand(readCommand);
    
    DateOnly FechaIncio, FechaFin;
    int TCiclo = 0;
    bool RedStdOut = false;

    readCommand.SetHandler(
        (_localRoot, _rutaScript, _rutaDb, _finicio, _ffin, _tCiclo, _redStdOut) =>
        {
            WKCopernicus._localRoot = _localRoot;
            WKCopernicus._rsScript = _rutaScript;
            WKCopernicus._RutaDb = _rutaDb;
            FechaIncio = _finicio;
            FechaFin = _ffin;
            TCiclo = -_tCiclo;
            RedStdOut = _redStdOut;
        },
        localRootOpt, rutaScriptOpt, rutaDbOpt, FInicioOpt, FFinOpt, TCicloOpt, RedStdOutOpt);
    
    if (RedStdOut) {
        var FOpciones =  new FileStreamOptions() {
            Mode = FileMode.Create,
            Share = FileShare.None,
            Access = FileAccess.Write
        };
        var LocStdOut = new StreamWriter("StdOut.txt", encoding: System.Text.Encoding.UTF8, FOpciones);
        Console.SetOut(LocStdOut);
    }
    var builder = new ConfigurationBuilder()
        .AddUserSecrets<Program>();
    var configurationRoot =  builder.Build();
    
    WKCopernicus._servidorRemoto = configurationRoot.GetValue<string>("servidorremoto");
    WKCopernicus._usuario = configurationRoot.GetValue<string>("usuario");
    WKCopernicus._contra = configurationRoot.GetValue<string>("contra");    

    using (var tLongo = new Timer(DoWork, null, new TimeSpan(0, 1, 0), new TimeSpan(0, TCiclo, 0))) {
    
        Console.WriteLine("Pulse <Enter> para salir ...");

        while(Console.ReadKey().Key != ConsoleKey.Enter);
        tLongo.Change(System.Threading.Timeout.Infinite, 1);
        int Repes = 0;
        
        while (0 != Interlocked.Exchange(ref UsandoRec, 1))
        {
            Thread.Sleep(1000);
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.WriteLine($"Esperando por procesos activos para terminar {Repes} segundos");
            Repes++;
        };
        Interlocked.Exchange(ref UsandoRec, 0);
        Console.WriteLine("Programa finalizado.");
    }
    return;
} catch (Exception e){
    Console.WriteLine(e.Message);
    Console.WriteLine("Programa finalizado.");
    return;
}

async void DoWork(object Objeto)
{
    if (0 != Interlocked.Exchange(ref UsandoRec, 1)) return;
   
    Console.WriteLine("Worker running at: {time}", DateTimeOffset.Now);
    await DownloadByMonth(tk);
    await MakeNclTotals(tk);
    
    Interlocked.Exchange(ref UsandoRec, 0);
}

static class WKCopernicus
{
    public static string _localRoot = string.Empty;
    public static string _RutaDb = string.Empty;
    public static string _rsScript = string.Empty;
    public static string _usuario = string.Empty;
    public static string _servidorRemoto = string.Empty;
    public static string _contra = string.Empty;

    static WKCopernicus()
    {       
    }    
    public static Task DownloadByMonth(ref RegistroPorMes RFecha, CancellationToken cToken, bool Actualizar = false)
    {        
        try {
            using (FtpClient FTPCliente = new(_servidorRemoto, _usuario, _contra)) {

                var DirectorioRaiz = _localRoot
                        + $"{RFecha.Año.ToString("D4")}/{RFecha.Mes.ToString("D2")}";
                var ResultFtp = new List<FtpResult>();                    

                string RutaCompleta = $"{_localRoot}/{RFecha.Año.ToString("D4")}/{RFecha.Mes.ToString("D2")}";
                Directory.CreateDirectory(RutaCompleta);
                FtpLocalExists Existe;

                if (!Actualizar)  Existe = FtpLocalExists.Skip;
                    else Existe = FtpLocalExists.Overwrite;

                var Profile = FTPCliente.AutoConnect();

                if (Profile != null && FTPCliente.IsConnected && FTPCliente.IsAuthenticated) {

                    var AntIndice = 0;

                    ResultFtp = FTPCliente.DownloadDirectory(RutaCompleta, DirectorioRaiz, FtpFolderSyncMode.Mirror, Existe, 
                        progress: (Progreso) => {
                            if (Progreso.FileIndex == AntIndice)
                                Console.WriteLine($"{Progreso.FileIndex} archivo/s terminado/s de {Progreso.FileCount}");
                            AntIndice = Progreso.FileIndex;
                        });
                    FTPCliente.Disconnect();
                    
                    for (int j = 0; j < ResultFtp.Count; j++) {

                        var locDato = new RegistroArchivo(){
                            RutaRemota = ResultFtp[j].RemotePath,
                            AñoMesDia = new DateOnly(RFecha.Año, RFecha.Mes, j)
                        };
                        if (ResultFtp[j].IsFailed) {                                 
                            locDato.Failed = true;
                        } else {
                            locDato.RutaLocal = ResultFtp[j].LocalPath;
                        }
                        RFecha.DatosArchivo.Add(locDato);
                    }
                    Console.WriteLine("Terminada la captura de datos FTP");

                } else {
                    if (FTPCliente.IsConnected) FTPCliente.Disconnect();
                    throw new Exception("No se pudo conectar");
                }
            }
            RFecha.Download = true;
            return Task.CompletedTask;

        } catch (Exception e) {
            RFecha.Download = false;
            Console.WriteLine(e.Message);
            return Task.FromException(e);
        }
    }
    public static Task MakeNclTotals(ref RegistroPorMes RFecha, CancellationToken cToken, 
        bool Actualizar = false)
    {
        try {
            SQLiteConnection DbCon = new( @$"Data Source={_RutaDb};Version=3;");
            DbCon.Open();
            using SQLiteCommand CommDb = DbCon.CreateCommand();
            CommDb.CommandText = "INSERT OR REPLACE INTO Main VALUES (@anhomesdia, @json, @sifnorte, @sifsur);";
            CommDb.Parameters.Clear();
            CommDb.Parameters.Add("@anhomesdia", System.Data.DbType.Date);
            CommDb.Parameters.Add("@json", System.Data.DbType.String);
            CommDb.Parameters.Add("@sifnorte", System.Data.DbType.Double);
            CommDb.Parameters.Add("@sifsur", System.Data.DbType.Double);
            CommDb.Prepare();

            var NScript = $"{_rsScript}/DRScript.ncl";
            var CScript = File.ReadAllLines(NScript);
           
            for (int i = 0; i < RFecha.DatosArchivo.Count; i++)
            {
                if (RFecha.DatosArchivo[i].Failed) {
                    Console.WriteLine($"Fichero de fecha {RFecha.DatosArchivo[i].AñoMesDia.ToString("yyyyMMdd")} falló en la descarga");
                    continue;
                }
                var LocScript = new string[CScript.Length];
                Array.Copy(CScript, LocScript, CScript.Length);

                LocScript[1] = LocScript[1].TrimStart();
                LocScript[1] = LocScript[1].Insert(13, RFecha.DatosArchivo[i].RutaLocal);

                LocScript[7] = LocScript[7].TrimStart();
                LocScript[7] = LocScript[7].Insert(13, $"{_rsScript}/SaveAscii.tmp");

                File.WriteAllLines($"{_rsScript}/DRScriptFilled.ncl", LocScript);

                 using (Process ProcesoNcl = new Process()) {
                    ProcesoNcl.StartInfo.FileName = "ncl";
                    ProcesoNcl.StartInfo.Arguments = $"{_rsScript}/DRScriptFilled.ncl";
                    ProcesoNcl.Start();
                    ProcesoNcl.WaitForExit(5000);
                    if (!ProcesoNcl.HasExited) ProcesoNcl.Kill(true);
                 }
                var TxtAscii = File.ReadAllLines($"{_rsScript}/SaveAscii.tmp");
                var Datos = TxtAscii[0].Split(' ', 2, StringSplitOptions.TrimEntries);

                var SExito = double.TryParse(Datos[0], out double SifN);
                if (SExito) RFecha.DatosArchivo[i].ToTalSeaIceFractionNorth = SifN;
                    else throw new Exception("Error en conversion datos");

                SExito = double.TryParse(Datos[1], out double SifS);
                if (SExito) RFecha.DatosArchivo[i].ToTalSeaIceFractionSouth = SifS;
                    else throw new Exception("Error en conversion datos");

                CommDb.Parameters["@anhomesdia"].Value = RFecha.DatosArchivo[i].AñoMesDia;                
                CommDb.Parameters["@json"].Value =
                    JsonSerializer.Serialize(RFecha.DatosArchivo);
                CommDb.Parameters["@sifnorte"].Value = SifN;
                CommDb.Parameters["@sifsur"].Value = SifS;
                var result = CommDb.ExecuteNonQuery();
                Task.Delay(500);
                Console.WriteLine($"{i} fichero/s terminado/s de {RFecha.DatosArchivo.Count}");
            }
            DbCon.Close();
            DbCon.Dispose();
            Console.WriteLine($"{RFecha.DatosArchivo.Count} ficheros teminados. Finalizado.");
            if (File.Exists($"{_rsScript}/DRScriptFilled.ncl"))
                File.Delete($"{_rsScript}/DRScriptFilled.ncl");
            if (File.Exists($"{_rsScript}/SaveAscii.tmp"))
                File.Delete($"{_rsScript}/SaveAscii.tmp");
            return Task.CompletedTask;

        } catch (Exception e){
            Console.WriteLine(e.Message);
            return Task.FromException(e);
        }
    }   
}

public class RegistroPorMes
{
    public int Año { get; set; } = 0;
    public int Mes { get; set; } = 0;
    public bool Download {get; set;} = false;
    public List<RegistroArchivo> DatosArchivo {get; set;} = new();    
}
public class RegistroArchivo
{
    public DateOnly AñoMesDia { get; set; } = DateOnly.MinValue;
    public string RutaLocal { get; set; } = string.Empty;
    public string RutaRemota { get; set; } = string.Empty;   
    public bool Failed {get; set;} = false;
    public double ToTalSeaIceFractionNorth { get; set; } = -1.0;
    public double ToTalMeanHeightNorth { get; set; } = -1.0;
    public double ToTalSeaIceFractionSouth { get; set; } = -1.0;
    public double ToTalMeanHeightSouth { get; set; } = -1.0;
}
