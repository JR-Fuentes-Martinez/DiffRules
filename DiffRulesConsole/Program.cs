using DiffRulesLib;
using FluentFTP;
using System.Diagnostics;
using System.Data.SQLite;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.CommandLine;
using System.IO;
using System.Security.Cryptography;

int NumCompletas = 0;

try {
    var builder = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>();
    var configurationRoot =  builder.Build();

    DateOnly FechaIncio = DateOnly.MinValue, FechaFin = DateOnly.MinValue;
    int TCiclo = 0;
    bool RedStdOut = false;
    int Meses = 0;

    if (RedStdOut) {
        var FOpciones =  new FileStreamOptions() {
            Mode = FileMode.Create,
            Share = FileShare.None,
            Access = FileAccess.Write
        };
        var LocStdOut = new StreamWriter("StdOut.txt", encoding: System.Text.Encoding.UTF8, FOpciones);
        Console.SetOut(LocStdOut);
    }    
    WKCopernicus._servidorRemoto = configurationRoot.GetValue<string>("servidorremoto");
    WKCopernicus._usuario = configurationRoot.GetValue<string>("usuario");
    WKCopernicus._contra = configurationRoot.GetValue<string>("contra");
    WKCopernicus._localRoot = configurationRoot.GetValue<string>("localroot");
    WKCopernicus._rsScript = configurationRoot.GetValue<string>("rutascript");
    WKCopernicus._RutaDb = configurationRoot.GetValue<string>("rutadb");
    TCiclo = configurationRoot.GetValue<int>("tciclo");
    FechaIncio = configurationRoot.GetValue<DateOnly>("fechainicio");
    FechaFin = configurationRoot.GetValue<DateOnly>("fechafin");

    for (DateOnly i = FechaIncio; i <= FechaFin; i=i.AddMonths(1)) { Meses++; }

    ObjTimer objTimer = new() {
        Meses = Meses,
        FechaInicio = FechaIncio
    };
    using (var tLongo = new Timer( DoWork, objTimer, new TimeSpan(0, 0, 30), new TimeSpan(0, TCiclo, 0))) {

        for (int i = 0; i < Meses; i++)
        {
            objTimer.AutoEvent.WaitOne();                 
        }
        tLongo.Change(System.Threading.Timeout.Infinite, 1);        
        Console.WriteLine("Programa finalizado.");
    }
    return;

} catch (Exception e) {
    Console.WriteLine(e.Message);
    Console.WriteLine("Programa finalizado.");
    return; 
}

void DoWork(object Objeto)
{
    var objTimer = (ObjTimer) Objeto;
    var Meses = objTimer.Meses;
    var FechaInicio = objTimer.FechaInicio;
    var AutoEvent = objTimer.AutoEvent;
   
    Console.WriteLine($"Worker running at: {DateTime.Now}");
    var FechaAct = FechaInicio.AddMonths(NumCompletas);   

    RegistroPorMes RPorMes = new() {
        Año = FechaAct.Year,
        Mes = FechaAct.Month
    };
    //await WKCopernicus.DownloadByMonth(ref RPorMes, Actualizar: false);
    //await WKCopernicus.MakeNclTotals(ref RPorMes, Actualizar: false);
    
    NumCompletas++;
    AutoEvent.Set();
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
    public static Task DownloadByMonth(ref RegistroPorMes RFecha, bool Actualizar = false)
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
    public static Task MakeNclTotals(ref RegistroPorMes RFecha, bool Actualizar = false)
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

public class ObjTimer
{
    public int Meses {get; set;} = 0;
    public DateOnly FechaInicio {get; set;} = DateOnly.MinValue;
    public AutoResetEvent AutoEvent {get; set;} = new(false);

}
