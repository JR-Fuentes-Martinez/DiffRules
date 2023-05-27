using DiffRulesLib;
using FluentFTP;
using FluentFTP.Client;
using System.Net;
using System.Diagnostics;
using System.Data.SQLite;
using System.Text.Json;

namespace DiffRulesWk;
public class WKCopernicus : BackgroundService
{
    private readonly ILogger<WKCopernicus> _logger;
    private readonly IConfiguration _config;
    private string _localRoot = string.Empty;
    private string _RutaScript = string.Empty;
    private string _RutaDb = string.Empty;


    public WKCopernicus(ILogger<WKCopernicus> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _RutaDb = _config.GetValue<string>("rutadb", string.Empty);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        //while (!stoppingToken.IsCancellationRequested)
        //{
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            RegistroPorMes RFecha = new() {
                Año = 2023,
                Mes = 04
            };
            await DownloadByMonth(ref RFecha, stoppingToken);
        //}
    }
    protected Task DownloadByMonth(ref RegistroPorMes RFecha, CancellationToken cToken, bool Actualizar = false)
    {        
        _localRoot = _config.GetValue<string>("localroot");
        
        try {
            using FtpClient FTPCliente = new( _config.GetValue<string>("ServidorRemoto"), _config.GetValue<string>("Usuario"),
                _config.GetValue<string>("Contra"));
                var Profile = FTPCliente.AutoConnect();
                
                if (Profile != null && FTPCliente.IsConnected && FTPCliente.IsAuthenticated) {

                    var DirectorioRaiz = _config.GetValue<string>("DirBase")
                            + $"{RFecha.Año.ToString("D4")}/{RFecha.Mes.ToString("D2")}";
                    var ResultFtp = new List<FtpResult>();

                    if (FTPCliente.DirectoryExists(DirectorioRaiz)) {

                        string RutaCompleta = $"{_localRoot}/{RFecha.Año.ToString("D4")}/{RFecha.Mes.ToString("D2")}";
                        Directory.CreateDirectory(RutaCompleta);
                        FTPCliente.SetWorkingDirectory(DirectorioRaiz);
                        FtpLocalExists Existe;

                        if (!Actualizar)  Existe = FtpLocalExists.Skip;
                            else Existe = FtpLocalExists.Overwrite;

                        ResultFtp = FTPCliente.DownloadDirectory(RutaCompleta, DirectorioRaiz, FtpFolderSyncMode.Mirror, Existe, 
                            progress: (Progreso) => _logger.LogInformation($"{Progreso.FileIndex} de {Progreso.FileCount} archivos terminados"));
                        FTPCliente.Disconnect();
                        
                        if (ResultFtp.Count == 0) {
                            return Task.CompletedTask;
                        }
                        RFecha.SoloRutaLocal = ResultFtp[0].LocalPath;
                        RFecha.SoloRutaRemota = ResultFtp[0].RemotePath;

                        for (int j = 0; j < ResultFtp.Count; j++)
                        {
                            var locDato = new RegistroArchivo();

                            if (ResultFtp[j].Exception != null) {                                 
                                locDato.Excepcion = ResultFtp[j].Exception;                               
                            }
                            locDato.SoloNombre = ResultFtp[j].Name;
                            RFecha.DatosArchivo.Add(locDato);
                        }                        
                    }  else throw new Exception("No existe el directorio raiz");                                             
                } else throw new Exception("No se pudo conectar");
            RFecha.ExitoDownload = true;
            return Task.CompletedTask;
        } catch (Exception e){
            RFecha.ExitoDownload = false;
            return Task.FromException(e);
        }
    }
    protected Task MakeNclTotals(ref RegistroPorMes RFecha, CancellationToken cToken, 
        bool Actualizar = false)
    {
        try {
            SQLiteConnection DbCon = new( @$"Data Source={_RutaDb};Version=3;");
            DbCon.Open();
            using SQLiteCommand CommDb = DbCon.CreateCommand();
            CommDb.CommandText = "INSERT OR ABORT INTO Main VALUES (@anho, @mes, @json);";
            CommDb.Parameters.Clear();
            CommDb.Parameters.Add("@anho", System.Data.DbType.Int32);
            CommDb.Parameters.Add("@mes", System.Data.DbType.Int32);
            CommDb.Parameters.Add("@json", System.Data.DbType.AnsiString);
            CommDb.Prepare();

            var NScript = $"{_config.GetValue<string>("rutascript")}/DRScript.ncl";
            var RScriptFilled = _config.GetValue<string>("rutascript");
            var CScript = File.ReadAllLines(NScript).ToList();

            using Process ProcesoNcl = new Process();
            ProcesoNcl.StartInfo.FileName = "ncl";
            var FSwriter = ProcesoNcl.StandardInput;
            ProcesoNcl.Start();
            Task.Delay(2000);

            for (int i = 0; i < RFecha.DatosArchivo.Count; i++)
            {
                var LocScript = CScript;

                LocScript[4] = LocScript[4].TrimStart();
                LocScript[4] = LocScript[4].Insert(12, 
                    $"{RFecha.SoloRutaLocal}/{RFecha.DatosArchivo[i].SoloNombre}");

                LocScript[12] = LocScript[12].TrimStart();
                LocScript[12] = LocScript[12].Insert(12, $"{RScriptFilled}/SaveAscii.tmp");

                File.WriteAllLines($"{RScriptFilled}/DRScriptFilled.ncl", LocScript);
                FSwriter.WriteLine($"load \"{RScriptFilled}/DRScriptFilled.ncl\"");
                FSwriter.WriteLine("TotalSIFCalc()");

                var TxtAscii = File.ReadAllLines($"{RScriptFilled}/SaveAscii.tmp").ToList();
                var Datos = TxtAscii[0].Split(' ', 2, StringSplitOptions.TrimEntries);

                var SExito = double.TryParse(Datos[0], out double SifN);
                if (SExito) RFecha.DatosArchivo[i].ToTalSeaIceFractionNorth = SifN;
                    else throw new Exception("Error en conversion datos");

                SExito = double.TryParse(Datos[1], out double SifS);
                if (SExito) RFecha.DatosArchivo[i].ToTalSeaIceFractionSouth = SifS;
                    else throw new Exception("Error en conversion datos");

                CommDb.Parameters["@anho"].Value = RFecha.Año;
                CommDb.Parameters["@mes"].Value = RFecha.Mes;
                CommDb.Parameters["@json"].Value =
                    JsonSerializer.Serialize(RFecha.DatosArchivo);
                var result = CommDb.ExecuteNonQuery();
            }
            ProcesoNcl.Kill(true);
            DbCon.Close();
            DbCon.Dispose();
            if (File.Exists($"{RScriptFilled}/DRScriptFilled.ncl"))
                File.Delete($"{RScriptFilled}/DRScriptFilled.ncl");
            if (File.Exists($"{RScriptFilled}/SaveAscii.tmp"))
                File.Delete($"{RScriptFilled}/SaveAscii.tmp");
            return Task.CompletedTask;

        } catch (Exception e){
            return Task.FromException(e);
        }        
    }
}
public class RegistroPorMes
{
    public int Año { get; set; } = 0;
    public int Mes { get; set; } = 0;
    public string SoloRutaLocal { get; set; } = string.Empty;
    public string SoloRutaRemota { get; set; } = string.Empty;    
    public bool ExitoDownload {get; set;} = false;
    public List<RegistroArchivo> DatosArchivo {get; set;} = new();    
}
public class RegistroArchivo
{
    public string SoloNombre { get; set; } = string.Empty;
    public Exception Excepcion { get; set; } = null;
    public double ToTalSeaIceFractionNorth { get; set; } = -1.0;
    public double ToTalMeanHeightNorth { get; set; } = -1.0;
    public double ToTalSeaIceFractionSouth { get; set; } = -1.0;
    public double ToTalMeanHeightSouth { get; set; } = -1.0;
}
