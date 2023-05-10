using Microsoft.Data.Analysis;
using System.Text;

namespace DiffRulesLib;

public class ClsMain
{
    public DataFrame InDatosSorted {get; set;} = null;
    public double Clave1 {get; set;} = double.NaN;
    public double Clave2 {get; set;} = double.NaN;
    public DataFrame Serie {get; set;} = null;
    public string NClave1 {get; set;} = string.Empty;
    string NClave2 {get; set;} = string.Empty;

    private List<DataFrame> Make_Diff_Breve_T(int Multiplo)
    {       
        long NumColumnas = InDatosSorted.Columns.Count;
        long NumFilas = InDatosSorted.Rows.Count;
        if (NumColumnas < 1) return null;
        List<DataFrame> outDatos = new(2);        
        
        for (int i = 0; i < NumColumnas; i++)
        {
            double[] Yes = new double[NumFilas];
            double[] Xes = new double[NumFilas];
            double[] X2es = new double[NumFilas * Multiplo];
            double[] f, d;

            for (int j = 0; j < NumFilas; j++)
            {
                Yes[j] = (double)InDatosSorted[j,i];
                Xes[j] = j;
            }            
            for (int j = 0; j < NumFilas * Multiplo; j++) {
                X2es[j] = (double)j / (double)Multiplo;
            }
            alglib.spline1dconvdiffcubic(Xes, Yes, Xes.Length, 0, 0.0, 0, 0.0, X2es, X2es.Length, out f, out d);
            double[] ffromd = new double[X2es.Length];
            double[] ferror = new double[X2es.Length];
            double Acunm = 0.0;

            for (int j = 0; j < ffromd.Length; j++)
            {
                Acunm += ffromd[j];
                ffromd[j] = Acunm;
                if (f[j] != 0) ferror[j] = (double)ffromd[j] / (double)f[j];
                    else ferror[j] = double.NaN;
            }            
            outDatos[0].Columns.Add(new DoubleDataFrameColumn(InDatosSorted.Columns[i].Name, ffromd)); 
            outDatos[0].Columns.Add(new DoubleDataFrameColumn("Ratio fd/f", ferror)); 
            outDatos[1].Columns.Add(new DoubleDataFrameColumn(InDatosSorted.Columns[i].Name, d));
        }
        return outDatos;        
    }

    public bool Make_Diff_Breve_T_S( string Ruta = "", int Multiplo = 1)
    {
        if (InDatosSorted == null || Serie == null) return false;
        if (InDatosSorted.Columns.Count < 1 || InDatosSorted.Rows.Count < 1) return false;
        if (Serie.Columns.Count < 2 || Serie.Rows.Count < 1) return false;
        List<DataFrame> outDatos =  new(2);
        var CulUsa = new System.Globalization.CultureInfo("en-US");
        
        for (int i = 0; i < Serie.Rows.Count; i++)        
        {
            var Datos = Make_Diff_Breve_T(Multiplo);
            var NumFilas = Datos[0].Rows.Count;

            Datos[0].Columns.Add(new DoubleDataFrameColumn(NClave1, NumFilas));
            Datos[0].Columns[NClave1].FillNulls(Clave1, true);
            Datos[1].Columns.Add(new DoubleDataFrameColumn(NClave1, NumFilas));
            Datos[1].Columns[NClave1].FillNulls(Clave1, true);
            Datos[0].Columns.Add(new DoubleDataFrameColumn(NClave2, NumFilas));
            Datos[0].Columns[NClave2].FillNulls(Clave2, true);
            Datos[1].Columns.Add(new DoubleDataFrameColumn(NClave1, NumFilas));
            Datos[1].Columns[NClave2].FillNulls(Clave2, true);

            Datos[0].Columns.Add(new DoubleDataFrameColumn(Serie[1, i].ToString(), NumFilas));
            Datos[0].Columns[Serie[1, i].ToString()].FillNulls(Serie[0, i], true);
            Datos[1].Columns.Add(new DoubleDataFrameColumn(Serie[1, i].ToString(), NumFilas));
            Datos[1].Columns[Serie[1, i].ToString()].FillNulls(Serie[0, i], true);   
            outDatos[0].Append(Datos[0].Rows, true);
            outDatos[1].Append(Datos[1].Rows, true);
        }
        try {
            DataFrame.SaveCsv(outDatos[0], Ruta, ';', true, Encoding.UTF8, CulUsa);        
            DataFrame.SaveCsv(outDatos[1], Ruta, ';', true, Encoding.UTF8, CulUsa);  
            return true;      
        } catch {
            return false;
        }
    }
}
