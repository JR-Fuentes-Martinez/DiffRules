﻿using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plotly.NET;
using Plotly.NET.ConfigObjects;  
using Plotly.NET.LayoutObjects;
using Plotly.NET.TraceObjects;
using Microsoft.FSharp.Core;
using System.IO.Compression;

namespace DiffRulesLib;

public enum TipoFuncion
{
    FuncionPrimitiva,
    Derivada,
    Error
}
public class ClsMain
{
    public void Make_Diff_Breve_T_S(ref DataFrame InDatosSorted, int Multiplo = 1)
    {
        var CulUsa = new System.Globalization.CultureInfo("en-US");  
        if (InDatosSorted == null ) return; 
        //if (Multiplo < 1 || Multiplo > 30) return;
        var NumFechas = InDatosSorted.DF.Count;      

        for (int j = 0; j < NumFechas; j++) {
            var NumMags = InDatosSorted.DF[j].Datos.Count;

            for (int k = 0; k < NumMags; k++) {
                var NumMag = InDatosSorted.DF[j].Datos[k].Mag.Length;

                double[] X2es = new double[NumMag * Multiplo];
                double[] Xes = new double[NumMag];
                double[] Yes = InDatosSorted.DF[j].Datos[k].Mag;
                double[] Serie = new double[X2es.Length]; 
                double[] f, d;

                for (int l = 0; l < Yes.Length; l++)
                {
                    Xes[l] = l;
                }
                for (int l = 0; l < X2es.Length; l++) {
                    X2es[l] = (double)l / (double)Multiplo;
                    Serie[l] = InDatosSorted.Series[j];
                }
                alglib.spline1dconvdiffcubic(Xes, Yes, Xes.Length, 0, 0.0, 0, 0.0, X2es, X2es.Length, out f, out d);                
                double[] ffromd = new double[X2es.Length];
                double[] ferror = new double[X2es.Length];
                var Acum = f[0] / Multiplo;
                ffromd[0] = Acum;
            
                for (int l = 1; l < X2es.Length; l++)
                {   
                    Acum += d[l];
                    ffromd[l] = Acum / Multiplo;
                    if (f[l] != 0) ferror[l] = Math.Abs((double)ffromd[l] / (double)f[l]);
                        else ferror[l] = double.MinValue;
                }
                Array.Resize(ref ffromd, ffromd.Length-1);
                InDatosSorted.DF[j].Datos[k].Derivada = d;
                InDatosSorted.DF[j].Datos[k].PrimitivaPorAcum = ffromd;
                InDatosSorted.DF[j].Datos[k].Error = ferror;
                InDatosSorted.DF[j].Datos[k].Tiempo = X2es;
                InDatosSorted.DF[j].Datos[k].Serie = Serie;
            }                            
        }
        InDatosSorted.Procesado = true;    
    }
    public bool Persist_DF_Json(ref DataFrame InDatosSorted, string Ruta)
    {
        try {
            InDatosSorted.Salvado = true;
            var cadena = JsonSerializer.Serialize(value: InDatosSorted, typeof(DataFrame));
            File.WriteAllText(Ruta, cadena);
            return true;
        } catch {
            return false;
        }
    }
    public GenericChart.GenericChart Make_Plotly_3DGraph(ref DataFrame InDatosSorted, int Mag, 
        GraphDefinition Definition, bool Salvar = false, string SoloRuta = "", 
        string SoloNombre = "graf")
    {             
        GenericChart.GenericChart[] ListaGraf = 
            new GenericChart.GenericChart[InDatosSorted.DF.Count]; 

        for (int i = 0; i <  InDatosSorted.DF.Count; i++)
        {
            List<double> LaFuncion = new();
            string Nombre = string.Empty;

            switch (Definition.Funcion)
            {
                case TipoFuncion.FuncionPrimitiva:
                    LaFuncion = InDatosSorted.DF[i].Datos[Mag].PrimitivaPorAcum.ToList();
                    Nombre = "Acum";
                    break;
                case TipoFuncion.Derivada:
                    LaFuncion = InDatosSorted.DF[i].Datos[Mag].Derivada.ToList();
                    Nombre = "Derivada";
                    break;
                case TipoFuncion.Error:
                    LaFuncion = InDatosSorted.DF[i].Datos[Mag].Error.ToList();
                    Nombre = "Error";
                    break;        
            }
            var bb = Chart3D.Chart.Line3D<double,double,double,double>(
                InDatosSorted.DF[i].Datos[Mag].Tiempo,
                InDatosSorted.DF[i].Datos[Mag].Serie,
                LaFuncion, LineColor: Color.fromString(Definition.ColorLinea),
                LineWidth: FSharpOption<double>.Some(3.0),
                Name: FSharpOption<string>.Some($"{Nombre}[{i}]")
            );            
            ListaGraf[i] = bb;
        }        
        var Total = Chart.Combine(ListaGraf)
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
                title: Title.init(FSharpOption<string>.Some(Definition.TituloX)), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithYAxisStyle(
                title: Title.init(FSharpOption<string>.Some(Definition.TituloY)), 
                Id: StyleParam.SubPlotId.Scene.NewScene(1))
            .WithZAxisStyle(
                title: Title.init(FSharpOption<string>.Some(Definition.TituloZ)));
            
        if (Salvar && !string.IsNullOrEmpty(SoloRuta)) {
            try
            {
                File.WriteAllText($"{SoloRuta}/graf.tmp", GenericChart.toChartHTML(Total));

                using (var Corriente = File.OpenWrite($"{SoloRuta}/{SoloNombre}.zip")) {
                    using (var Archivo = new ZipArchive(Corriente, ZipArchiveMode.Create)) {
                        Archivo.CreateEntryFromFile($"{SoloRuta}/graf.tmp", $"{SoloNombre}.txt");
                    }
                    Corriente.Close();
                }
                File.Delete($"{SoloRuta}/graf.tmp");
            }
            catch (System.Exception)
            {
                return null;
            }
        }
        return Total;
    }
}

public class GraphDefinition
{
    public int Ancho {get; set;} = 600;
    public int Alto {get; set;} = 600;
    public double Margen {get; set;} = 0.2;
    public double MargenTop {get; set;} = 0.2;
    public TipoFuncion Funcion {get; set;} = TipoFuncion.Derivada;
    public string TituloX {get; set;} = string.Empty;
    public string TituloY {get; set;} = string.Empty;
    public string TituloZ {get; set;} = string.Empty;
    public string Titulo {get; set;} = string.Empty;
    public double SizeTitulo {get; set;} = 16.0;
    public string ColorLinea {get; set;} = "brown";
}

public class DataSerie
{
    public double[] Mag {get; set;}
    public double[] Derivada {get; set;} 
    public double[] PrimitivaPorAcum {get; set;}
    public double[] Error {get; set;} 
    public double[] Tiempo {get; set;}
    public double[] Serie {get; set;} 

}
public class DataSeries
{
    public List<DataSerie> Datos {get; set;} = new();    
}
public class DataFrame
{
    public double Clave1 {get; set;} = double.MinValue;
    public double Clave2 {get; set;} = double.MinValue;
    public List<DataSeries> DF {get; set;} = new();
    public double[] Series {get; set;}
    public bool Procesado {get; set;} = false;
    public bool Salvado {get; set;} = false;
}