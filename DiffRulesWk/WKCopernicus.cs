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
    private string _rsScript = string.Empty;

    public WKCopernicus(ILogger<WKCopernicus> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _RutaDb = _config.GetValue<string>("rutadb", string.Empty);
        _rsScript = _config.GetValue<string>("rutascript");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        //while (!stoppingToken.IsCancellationRequested)
        //{
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            RegistroPorMes RFecha = new() {
                Año = 2023,
                Mes = 3
            };
            await DownloadByMonth(ref RFecha, stoppingToken);
            await MakeNclTotals(ref RFecha, stoppingToken);
        //}
    }
    protected Task DownloadByMonth(ref RegistroPorMes RFecha, CancellationToken cToken, bool Actualizar = false)
    {        
        _localRoot = _config.GetValue<string>("localroot");
        
        try {
            using (FtpClient FTPCliente = new(_config.GetValue<string>("ServidorRemoto"), _config.GetValue<string>("Usuario"), 
                _config.GetValue<string>("Contra"))) {

                var DirectorioRaiz = _config.GetValue<string>("DirBase")
                        + $"{RFecha.Año.ToString("D4")}/{RFecha.Mes.ToString("D2")}";
                var ResultFtp = new List<FtpResult>();                    

                string RutaCompleta = $"{_localRoot}/{RFecha.Año.ToString("D4")}/{RFecha.Mes.ToString("D2")}";
                Directory.CreateDirectory(RutaCompleta);
                FtpLocalExists Existe;

                if (!Actualizar)  Existe = FtpLocalExists.Skip;
                    else Existe = FtpLocalExists.Overwrite;

                var Profile = FTPCliente.AutoConnect();

                if (Profile != null && FTPCliente.IsConnected && FTPCliente.IsAuthenticated) {

                    ResultFtp = FTPCliente.DownloadDirectory(RutaCompleta, DirectorioRaiz, FtpFolderSyncMode.Mirror, Existe, 
                        progress: (Progreso) => _logger.LogInformation($"{Progreso.FileIndex} de {Progreso.FileCount} archivos terminados"));

                    FTPCliente.Disconnect();
                    
                    if (ResultFtp.Count == 0) {
                        return Task.CompletedTask;
                    }
                    for (int j = 0; j < ResultFtp.Count; j++)
                    {
                        var locDato = new RegistroArchivo();

                        if (ResultFtp[j].Exception != null) {                                 
                            locDato.Excepcion = ResultFtp[j].Exception;                               
                        }
                        locDato.RutaLocal = ResultFtp[j].LocalPath;
                        locDato.RutaRemota = ResultFtp[j].RemotePath;
                        RFecha.DatosArchivo.Add(locDato);
                    }
                } else {
                    if (FTPCliente.IsConnected) FTPCliente.Disconnect();
                    throw new Exception("No se pudo conectar");
                }
            }
            RFecha.ExitoDownload = true;
            return Task.CompletedTask;

        } catch (Exception e) {
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
            CommDb.Parameters.Add("@json", System.Data.DbType.String);
            CommDb.Prepare();

            var NScript = $"{_rsScript}/DRScript.ncl";
            var CScript = File.ReadAllLines(NScript);
           
            for (int i = 0; i < RFecha.DatosArchivo.Count; i++)
            {
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
                    ProcesoNcl.WaitForExit(7500);
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

                CommDb.Parameters["@anho"].Value = RFecha.Año;
                CommDb.Parameters["@mes"].Value = RFecha.Mes;
                CommDb.Parameters["@json"].Value =
                    JsonSerializer.Serialize(RFecha.DatosArchivo);
                var result = CommDb.ExecuteNonQuery();
                Task.Delay(1000);
            }
            DbCon.Close();
            DbCon.Dispose();
            if (File.Exists($"{_rsScript}/DRScriptFilled.ncl"))
                File.Delete($"{_rsScript}/DRScriptFilled.ncl");
            if (File.Exists($"{_rsScript}/SaveAscii.tmp"))
                File.Delete($"{_rsScript}/SaveAscii.tmp");
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
    public bool ExitoDownload {get; set;} = false;
    public List<RegistroArchivo> DatosArchivo {get; set;} = new();    
}
public class RegistroArchivo
{
    public string RutaLocal { get; set; } = string.Empty;
    public string RutaRemota { get; set; } = string.Empty;    
    public Exception Excepcion { get; set; } = null;
    public double ToTalSeaIceFractionNorth { get; set; } = -1.0;
    public double ToTalMeanHeightNorth { get; set; } = -1.0;
    public double ToTalSeaIceFractionSouth { get; set; } = -1.0;
    public double ToTalMeanHeightSouth { get; set; } = -1.0;
}
