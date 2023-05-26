using DiffRulesLib;
using FluentFTP;
using FluentFTP.Client;
using System.Net;

namespace DiffRulesWk;
public class WKCopernicus : BackgroundService
{
    private readonly ILogger<WKCopernicus> _logger;
    private readonly IConfiguration _config;
    private string _localRoot = string.Empty;

    public WKCopernicus(ILogger<WKCopernicus> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
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
            using FtpClient FTPCliente = new("nrt.cmems-du.eu", _config.GetValue<string>("Usuario"),
                _config.GetValue<string>("Contra"));
                var Profile = FTPCliente.AutoConnect();
                
                if (Profile != null && FTPCliente.IsConnected && FTPCliente.IsAuthenticated) {

                    var DirectorioRaiz = "/Core/SST_GLO_SST_L4_NRT_OBSERVATIONS_010_001/METOFFICE-GLO-SST-L4-NRT-OBS-SST-V2/"
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
}
public class RegistroPorMes
{
    public int Año { get; set; } = 0;
    public int Mes { get; set; } = 0;
    public string SoloRutaLocal { get; set; } = string.Empty;
    public string SoloRutaRemota { get; set; } = string.Empty;
    public double ToTalSeaIceFractionNorth { get; set; } = -1.0;
    public double ToTalMeanHeightNorth { get; set; } = -1.0;
    public double ToTalSeaIceFractionSouth { get; set; } = -1.0;
    public double ToTalMeanHeightSouth { get; set; } = -1.0;
    public bool ExitoDownload {get; set;} = false;
    public List<RegistroArchivo> DatosArchivo {get; set;} = new();    
}
public class RegistroArchivo
{
    public string SoloNombre { get; set; } = string.Empty;
    public Exception Excepcion { get; set; } = null;
}
