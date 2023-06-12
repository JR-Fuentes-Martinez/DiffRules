using Microsoft.Data.Analysis;
using FluentFTP;
using System.IO.Compression;
using Plotly.NET;
using Plotly.NET.LayoutObjects;
using Microsoft.FSharp.Core;
using System.Diagnostics;
using System.Text;

namespace NcidWorkerSpc;

public class NcidWorker : BackgroundService
{   
    private readonly ILogger<NcidWorker> _logger;
    private readonly IConfiguration _conf;
    private readonly IHostApplicationLifetime _lftime;
    private readonly string RutaDb = $"Data Source={Directory.GetCurrentDirectory()}/MainDb.db;version=3;";
    private string HostName, UserName, Password, DirRemotoN, DirRemotoS, DirLocal, Archivo;
    private int AñoDeInicio = 1978;

    public NcidWorker(ILogger<NcidWorker> logger, IConfiguration conf, IHostApplicationLifetime lftime)
    {
        _logger = logger;
        _conf = conf;
        _lftime = lftime;       
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {   
        try {
            HostName = _conf.GetRequiredSection("Entradas").GetValue<string>("hostname"); 
            UserName = _conf.GetValue<string>("usuario"); // de user-secrets
            Password = _conf.GetValue<string>("password"); // de user-secrets
            DirRemotoN = _conf.GetRequiredSection("Entradas").GetValue<string>("dirremotoN"); 
            DirRemotoS = _conf.GetRequiredSection("Entradas").GetValue<string>("dirremotoS"); 
            DirLocal = _conf.GetRequiredSection("Entradas").GetValue<string>("dirlocal");      
            Archivo = _conf.GetRequiredSection("Entradas").GetValue<string>("archivo");      

            _logger.LogInformation("Recuperando datos FTP");
            //await DownloadData();  

            _logger.LogInformation("Comenzando transformación de datos");

            var LaRuta = $"{DirLocal}\\N{Archivo}";
            await MakeDataFrameAndSave(LaRuta, "Norte");

            LaRuta = $"{DirLocal}\\S{Archivo}";
            await MakeDataFrameAndSave(LaRuta, "Sur");

             _logger.LogInformation("Terminado\n");

        } catch (Exception e){
            if (e.InnerException != null)_logger.LogError(e.InnerException.Message);
                else _logger.LogError(e.Message);
        } finally {
            _lftime.StopApplication();        
        }        
    }
    private Task DownloadData()
    {        
        try {
            using (var FTPCon = new FtpClient(HostName, user: UserName, pass: Password))
            {
                FTPCon.AutoConnect();
                FTPCon.DownloadFile($"{DirLocal}\\N{Archivo}", $"{DirRemotoN}/N{Archivo}");
                FTPCon.DownloadFile($"{DirLocal}\\S{Archivo}", $"{DirRemotoS}/S{Archivo}");
                FTPCon.Disconnect();
                FTPCon.Dispose();
            }
            if (!File.Exists($"{DirLocal}\\N{Archivo}")) throw new Exception("Error en descarga FTP");               
            if (!File.Exists($"{DirLocal}\\S{Archivo}")) throw new Exception("Error en descarga FTP");               

            return Task.CompletedTask;
            
        } catch (Exception e) {
            return Task.FromException(e);
        }
    }
    private Task MakeDataFrameAndSave(string Ruta, string Hemisferio)
    {
        try {
            var DataTxt = File.ReadAllLines(Ruta);
            DataTxt = DataTxt.Skip(2).ToArray();
            var FicheroCut = Path.GetTempFileName();
            File.WriteAllLines(FicheroCut, DataTxt);
            Array.Clear(DataTxt);
            var Cultura = Thread.CurrentThread.CurrentCulture;
            
            var Nombres = new string[] {
                "Year", "Month", "Day", "Extent", "Missing", "Source Data"
            };
            var Tipos = new Type[] {typeof(int), typeof(int), typeof(int), typeof(double), typeof(double), typeof(string)};

            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            var AllData = DataFrame.LoadCsv(FicheroCut, header: false, columnNames: Nombres, dataTypes: Tipos);
            Thread.CurrentThread.CurrentCulture = Cultura;
            if (File.Exists(FicheroCut)) File.Delete(FicheroCut);

            var ColumnaX = AllData.Columns[0];
            var ColumnaY = AllData.Columns[2];
            var ColumnaZ = AllData.Columns[3];
            var ColMes = AllData.Columns[1];
            var DFOut = new DataFrame(ColumnaX, ColumnaY, ColumnaZ, ColMes);
            CalcDerivative(ref DFOut);

            var RutaTemp = Directory.CreateDirectory($"{Path.GetTempPath()}/{Path.GetRandomFileName()}");

            var FicheroTemp = File.OpenWrite($"{RutaTemp.FullName}/zipped.txt");
            DataFrame.SaveCsv(DFOut, FicheroTemp, separator: ';', encoding: System.Text.Encoding.UTF8, 
                cultureInfo: new System.Globalization.CultureInfo("en-US"));
            FicheroTemp.Dispose();

            string NomZipped = $"{DirLocal}/{DateTime.Now:yyyyMMdd}-" +  Hemisferio.ToUpper() +
                "-Sea_Ice_DataFrame-zipped.zip";

            if (File.Exists(NomZipped)) File.Delete(NomZipped);
            ZipFile.CreateFromDirectory(RutaTemp.FullName, NomZipped, compressionLevel: CompressionLevel.Optimal, false);

            MakePlotly3DGraph(ref DFOut, new GraphDefinition(), _conf.GetRequiredSection("Entradas").GetValue<int>("fechainicio"),
                _conf.GetRequiredSection("Entradas").GetValue<int>("fechafin"), Hemisferio);
           
            return Task.CompletedTask;

        } catch (Exception e) {
            return Task.FromException(e);
        }        
    }
    private Task CalcDerivative(ref DataFrame DF)
    {
        var abscisas = new double[DF.Rows.Count];
        var ordenadas = new double[DF.Rows.Count];        
        var Positivos = new double[DF.Rows.Count];
        var Negativos = new double[DF.Rows.Count];
        
        var Penalizacion = _conf.GetRequiredSection("Entradas").GetValue<double>("penalizacion");

        for (int i = 0; i < DF.Rows.Count; i++)
        {
            abscisas[i] = i;
            ordenadas[i] = (double)DF[i, 2];
        }
        alglib.spline1dconvdiffcubic(abscisas, ordenadas, abscisas.Length, 0, 0.0, 0, 0.0, abscisas, abscisas.Length, out double[] f, out double[] d);
        alglib.spline1dfitpenalized(abscisas, d, 200, Penalizacion, out int info ,out alglib.spline1dinterpolant s, out alglib.spline1dfitreport rep);
        
        for (int i = 0; i < abscisas.Length ; i++) {
            d[i] = alglib.spline1dcalc(s, abscisas[i]) * 10e6;
            if (Math.Sign(d[i]) < 0) {
                Negativos[i] = d[i];
                Positivos[i] = 0.0;
            } else {
                Negativos[i] = 0.0;
                Positivos[i] = d[i];
            }
        }
        alglib.spline1dconvdiffcubic(abscisas, d, abscisas.Length, 0, 0.0, 0, 0.0, abscisas, abscisas.Length, out double[] f1, out double[] d2);
        
        double[] ffromd = new double[abscisas.Length];
        double[] ferror = new double[abscisas.Length];
        var Acum = f1[0];
        ffromd[0] = Acum;

        for (int l = 1; l < abscisas.Length; l++)
        {   
            Acum += d2[l];
            ffromd[l] = Acum;
            if (f[l] != 0) ferror[l] = Math.Abs((double)f1[l] / (double)ffromd[l] / 10e6);
                else ferror[l] = double.MinValue;
        }
        var ColDerivada = new DoubleDataFrameColumn("Derivada", d2);
        var ColFAcum = new DoubleDataFrameColumn("FuncionAcum", ffromd);
        var ColError = new DoubleDataFrameColumn("Error", ferror);
        var ColPos = new DoubleDataFrameColumn("Positivos", ferror);
        var ColNegs = new DoubleDataFrameColumn("Nrgativos", ferror);
        var DExtended = new DataFrame(ColDerivada, ColFAcum, ColError, ColPos, ColNegs);

        Array.Clear(d);
        Array.Clear(f);
        Array.Clear(ffromd);
        Array.Clear(ferror);
        Array.Clear(abscisas);
        Array.Clear(ordenadas);
        DF = DF.Join(DExtended);

        return Task.CompletedTask;
    }
    private Task MakePlotly3DGraph(ref DataFrame Datos, GraphDefinition Definition, 
        int AñoInicio, int AñoFin, string Hemisferio, string SoloNombre = "graf")
    {
        
        var Grupos = Datos.GroupBy<int>("Year").Groupings;
        Grupos = Grupos.SkipWhile((Grupo) => Grupo.Key < AñoInicio).TakeWhile((Grupo) => Grupo.Key <= AñoFin);

        var GrafDerivada = new List<GenericChart.GenericChart>(Grupos.Count()); 
        var GrafFAcum = new List<GenericChart.GenericChart>(Grupos.Count()); 
        var GrafError = new List<GenericChart.GenericChart>(Grupos.Count()); 

        foreach (var Grupo in Grupos)
        {
            var arrDerivada = new double[Grupo.Count()];
            var arrFAcum = new double[Grupo.Count()];
            var arrError = new double[Grupo.Count()];
            var arrDias = new double[Grupo.Count()];
            var arrSeries = new double[Grupo.Count()];
            int Indice = 0;

            foreach(var DRow in Grupo)
            {
                arrDerivada[Indice] = (double)DRow[4];
                arrFAcum[Indice] = (double)DRow[5];
                arrError[Indice] = (double)DRow[6];
                arrSeries[Indice] = (int)DRow[0];
                arrDias[Indice] = Indice+1;
                Indice++;
            }           
            var GDerivada = Chart3D.Chart.Line3D<double,double,double,double>(arrDias, arrSeries, arrDerivada, 
                LineColor: Color.fromString(Definition.ColorLinea), LineWidth: FSharpOption<double>.Some(3.0),
                Name: FSharpOption<int>.Some(AñoDeInicio).ToString());
            GrafDerivada.Add(GDerivada);

            var GFAcum = Chart3D.Chart.Line3D<double,double,double,double>(arrDias, arrSeries, arrFAcum, 
                LineColor: Color.fromString(Definition.ColorLinea), LineWidth: FSharpOption<double>.Some(3.0),
                Name: FSharpOption<int>.Some(AñoDeInicio).ToString());
            GrafFAcum.Add(GFAcum);

            var GError = Chart3D.Chart.Line3D<double,double,double,double>(arrDias, arrSeries, arrError, 
                LineColor: Color.fromString(Definition.ColorLinea), LineWidth: FSharpOption<double>.Some(3.0),
                Name: FSharpOption<int>.Some(AñoDeInicio).ToString());
            GrafError.Add(GError);
            AñoDeInicio++;
        }                      
        var TDerivada = Chart.Combine(GrafDerivada)
            .WithLayout(Layout.init<bool>(
                ShowLegend: FSharpOption<bool>.Some(false),
                Margin: FSharpOption<Margin>.Some(
                    Margin.init<double, double, double, double, double, bool>(
                        Left: Definition.Margen, Right: Definition.Margen, Top: Definition.MargenTop, Bottom: Definition.Margen, 
                        Pad: 0.0, Autoexpand: FSharpOption<bool>.None))))
            .WithConfig(Config.init(Responsive: FSharpOption<bool>.Some(true), FillFrame: FSharpOption<bool>.None,
                Autosizable: FSharpOption<bool>.None))
            .WithSize(Definition.Ancho, Definition.Alto)
            .WithXAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Days")), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithYAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Years")), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithZAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Derivada ( sq2:day:day )")));
        
        var TFAcum = Chart.Combine(GrafFAcum)
            .WithLayout(Layout.init<bool>(
                ShowLegend: FSharpOption<bool>.Some(false),
                Margin: FSharpOption<Margin>.Some(
                    Margin.init<double, double, double, double, double, bool>(
                        Left: Definition.Margen, Right: Definition.Margen, Top: Definition.MargenTop, Bottom: Definition.Margen, 
                        Pad: 0.0, Autoexpand: FSharpOption<bool>.None))))
            .WithConfig(Config.init(Responsive: FSharpOption<bool>.Some(true), FillFrame: FSharpOption<bool>.None,
                Autosizable: FSharpOption<bool>.None))
            .WithSize(Definition.Ancho, Definition.Alto)
            .WithXAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Days")), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithYAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Years")), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithZAxisStyle(
                title: Title.init(FSharpOption<string>.Some("FAcum ( sq2:day )")));

        var TError = Chart.Combine(GrafError)
            .WithLayout(Layout.init<bool>(
                ShowLegend: FSharpOption<bool>.Some(false),
                Margin: FSharpOption<Margin>.Some(
                    Margin.init<double, double, double, double, double, bool>(
                        Left: Definition.Margen, Right: Definition.Margen, Top: Definition.MargenTop, Bottom: Definition.Margen, 
                        Pad: 0.0, Autoexpand: FSharpOption<bool>.None))))
            .WithConfig(Config.init(Responsive: FSharpOption<bool>.Some(true), FillFrame: FSharpOption<bool>.None,
                Autosizable: FSharpOption<bool>.None))
            .WithSize(Definition.Ancho, Definition.Alto)
            .WithXAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Days")), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithYAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Years")), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithZAxisStyle(
                title: Title.init(FSharpOption<string>.Some("Error ( ./. )")));
               
            try {
                TDerivada.SaveHtml($"{Directory.GetCurrentDirectory()}/x64/{Hemisferio}.html");
                //BeautifyHtml($"{Directory.GetCurrentDirectory()}/x64/{Hemisferio}.html");

                if (_conf.GetRequiredSection("Entradas").GetValue<bool>("mostrar")) {
                    using (Process brave = new Process()) {
                        brave.StartInfo.FileName = "\"C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe\"";
                        brave.StartInfo.Arguments = $"--incognito {Directory.GetCurrentDirectory()}/x64/{Hemisferio}.html";
                        brave.Start();
                    }
                }
            } catch (Exception e) {
                return Task.FromException(e);
            }
       
        return Task.CompletedTask;
    }

    private Task BeautifyHtml(string RutaCompleta) 
    {
        try {
            var Fichero = File.ReadAllLines(RutaCompleta);
            var FicheroHead = new string[4];
            //FicheroHead[0] = "<script src=\"https://cdn.plot.ly/plotly-2.24.1.min.js\" charset=\"utf-8\"></script>";
            Array.ConstrainedCopy(Fichero, 2, FicheroHead, 0, 4);
            var Ini = FicheroHead[3].IndexOf('\'') + 1;
            var Fin = FicheroHead[3].LastIndexOf('\'');
            var IdDiv = FicheroHead[3].Substring(Ini, Fin-Ini);
            FicheroHead[3] = FicheroHead[3].Replace(IdDiv, "MyDiv");            
            File.WriteAllLines(RutaCompleta, FicheroHead);
            return Task.CompletedTask;
        } catch (Exception e) {
            return Task.FromException(e);
        }        
    }
}

public class GraphDefinition
{
    public int Ancho {get; set;} = 800;
    public int Alto {get; set;} = 800;
    public double Margen {get; set;} = 0.2;
    public double MargenTop {get; set;} = 0.2;
    public string Titulo {get; set;} = string.Empty;
    public double SizeTitulo {get; set;} = 16.0;
    public string ColorLinea {get; set;} = "brown";
}
