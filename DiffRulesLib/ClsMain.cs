using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plotly.NET.CSharp;
using Plotly.NET.ConfigObjects;  
using Plotly.NET.LayoutObjects;
using Plotly.NET.TraceObjects;

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
        if (Multiplo < 1 || Multiplo > 30) return;
        var NumFechas = InDatosSorted.DF.Count;      

        for (int j = 0; j < NumFechas; j++) {
            var NumMags = InDatosSorted.DF[j].Datos.Count;

            for (int k = 0; k < NumMags; k++) {
                var NumMag = InDatosSorted.DF[j].Datos[k].Mag.Length;

                double[] X2es = new double[NumMag * Multiplo];
                double[] Xes = new double[NumMag];
                double[]? Yes = InDatosSorted.DF[j].Datos[k].Mag;
                double[] f, d;

                for (int l = 0; l < Yes.Length; l++)
                {
                    Xes[l] = l;
                }
                for (int l = 0; l < X2es.Length; l++) {
                    X2es[l] = (double)l / (double)Multiplo;
                }                    
                alglib.spline1dconvdiffcubic(Xes, Yes, Xes.Length, 0, 0.0, 0, 0.0, X2es, X2es.Length, out f, out d);
                double[] ffromd = new double[X2es.Length];
                double[] ferror = new double[X2es.Length];
                var Acum = f[0];
                ffromd[0] = Acum;
            
                for (int l = 1; l < X2es.Length; l++)
                {   
                    Acum += d[l];
                    ffromd[l] = Acum;
                    if (f[l] != 0) ferror[l] = (double)ffromd[l] / (double)f[l];
                        else ferror[l] = double.MinValue;
                }
                InDatosSorted.DF[j].Datos[k].Derivada = d;
                InDatosSorted.DF[j].Datos[k].PrimitivaPorAcum = ffromd;
                InDatosSorted.DF[j].Datos[k].Error = ferror;
                InDatosSorted.DF[j].Datos[k].Tiempo = X2es;
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
    public Plotly.NET.GenericChart.GenericChart Make_Plotly_3DGraph(ref DataFrame InDatosSorted, int Serie, 
        int Mag, TipoFuncion Func = TipoFuncion.Derivada  ) {    

        double[] FuncionZ;

        if (Func == TipoFuncion.Derivada) {
            FuncionZ = InDatosSorted.DF[Serie].Datos[Mag].Derivada;
        } else if (Func == TipoFuncion.FuncionPrimitiva) {
            FuncionZ = InDatosSorted.DF[Serie].Datos[Mag].PrimitivaPorAcum;
        } else {
            FuncionZ = InDatosSorted.DF[Serie].Datos[Mag].Error;
        }
        
        var Grafico = Chart.Scatter3D<double,double,double,string>(
            InDatosSorted.Series, InDatosSorted.DF[Serie].Datos[Mag].Tiempo,
            FuncionZ, Plotly.NET.StyleParam.Mode.Lines, ShowLegend: false);
            //, LineColor: new Optional<Plotly.NET.Color>(Plotly.NET.Color.fromString("blue"),true), 
            //LineWidth: 2, Text: "Evolución Breve_T_S");public
        return Grafico;
    }
}

public class DataSerie
{
    public double[] Mag {get; set;}
    public double[] Derivada {get; set;} 
    public double[] PrimitivaPorAcum {get; set;}
    public double[] Error {get; set;} 
    public double[] Tiempo {get; set;}
}
public class DataSeries
{
    public double Serie {get; set;} = double.MinValue;
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