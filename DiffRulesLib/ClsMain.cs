using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiffRulesLib;

public class ClsMain
{
    public void Make_Diff_Breve_T_S(ref DataFrame InDatosSorted, int Multiplo)
    { 
        var CulUsa = new System.Globalization.CultureInfo("en-US");   
        if (InDatosSorted.Serie.Length != InDatosSorted.ColumnasIn.Count) return;
        if (InDatosSorted == null || InDatosSorted.Serie == null) return;
        if (InDatosSorted.ColumnasIn.Count < 1) return;
        if (InDatosSorted.Serie.Length < 1)   return;
        var NumSeries = InDatosSorted.Serie.Length;

        for (int s = 0; s < NumSeries; s++)
        {
            var NumColumnas = InDatosSorted.ColumnasIn.Count;        
            var Columna = InDatosSorted.ColumnasIn[s];
            var NumFilas = Columna[s].Length;
            var ColumnaOut = new List<double[]>(NumFilas * Multiplo);
            double[] X2es = new double[NumFilas * Multiplo];            

            for (int j = 0; j < NumFilas * Multiplo; j++) {
                X2es[j] = (double)j / (double)Multiplo;
            }            
            for (int i = 0; i < NumColumnas; i++)
            {
                double[] Yes = new double[NumFilas];
                double[] Xes = new double[NumFilas];
                double[] f, d;

                for (int j = 0; j < NumFilas; j++)
                {
                    Yes[j] = Columna[i][j];
                    Xes[j] = j;
                }                
                alglib.spline1dconvdiffcubic(Xes, Yes, Xes.Length, 0, 0.0, 0, 0.0, X2es, X2es.Length, out f, out d);
                double[] ffromd = new double[X2es.Length];
                double[] ferror = new double[X2es.Length];
                double Acum = f[0];

                for (int j = 0; j < ffromd.Length; j++)
                {
                    Acum += d[j];
                    ffromd[j] = Acum;
                    if (f[j] != 0) ferror[j] = (double)ffromd[j] / (double)f[j];
                        else ferror[j] = double.MinValue;
                }
                ColumnaOut.Add(ffromd);
                ColumnaOut.Add(ferror);
                ColumnaOut.Add(d);
            }
            InDatosSorted.ColumnasOut.Add(ColumnaOut);
        }
        InDatosSorted.Procesado = true;
    }

    public void Persist_DF_Json(ref DataFrame InDatosSorted, string Ruta = "")
    {
        try {
            InDatosSorted.Salvado = true;
            var cadena = JsonSerializer.Serialize(value: InDatosSorted, typeof(DataFrame));
            File.WriteAllText(Ruta, cadena);
        } catch {
            InDatosSorted.Salvado = false;
        }
    }
}
public class DataFrame
{
    public double[] Serie {get; set;}
    public List<List<double[]>> ColumnasIn {get; set;} = new();
    public List<List<double[]>> ColumnasOut {get; set;} = new();
    public double Clave1Double {get; set;} = 0.0;
    public double Clave2Double {get; set;} = 0.0;
    public DateTime Clave3DateTime {get; set;} = DateTime.MinValue;
    public string Coletilla {get; set;} = string.Empty;    
    public bool Procesado {get; set;} = false;
    public bool Salvado {get; set;} = false;

}
