using DiffRulesLib;
using Plotly.NET;
using Microsoft.FSharp.Core;

var Magnitud = new double[20];
for (int i = 0; i < Magnitud.Length; i++)
{
    Magnitud[i] = Math.Pow(i, 2);
}
var Datos = new DataFrame();
Datos.Clave1 = 0.0;
Datos.Clave2 = 0.0;
Datos.Series = new double[] {1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0};

for (int j = 0; j < Datos.Series.Length; j++) {
    var DS = new DataSeries();

    for (int i = 0; i < 2; i++)
    {
        DataSerie Data1 = new DataSerie() {
            Mag =Magnitud
        };
        DS.Datos.Add(Data1);
    }
    Datos.DF.Add(DS);
}
var Clase = new ClsMain();
Clase.Make_Diff_Breve_T_S(ref Datos, 1);
//Clase.Persist_DF_Json(ref Datos, "/home/jr-fuentes-martinez/Documentos/DiffRules/persist.json");
GraphDefinition gd = new() {
    Alto = 800,
    Ancho = 800,
    Funcion = TipoFuncion.Error
};
var Grafico = Clase.Make_Plotly_3DGraph(ref Datos, 0, gd);
Grafico.Show();

