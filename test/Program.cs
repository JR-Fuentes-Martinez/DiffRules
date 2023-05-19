using DiffRulesLib;
using Plotly.NET.CSharp;

var Magnitud = new double[20];
for (int i = 0; i < Magnitud.Length; i++)
{
    Magnitud[i] = Math.Pow(i, 2);
}
var Datos = new DataFrame();
Datos.Clave1 = 0.0;
Datos.Clave2 = 0.0;
Datos.Series = new double[] {1.0, 2.0, 3.0};

for (int j = 0; j < 3; j++) {
    var DS = new DataSeries();

    for (int i = 0; i < 2; i++)
    {
        DataSerie Data1 = new DataSerie() {
            Mag =Magnitud
        };
        DS.Datos.Add(Data1);
    }
    DS.Serie = Datos.Series[j];
    Datos.DF.Add(DS);
}
var Clase = new ClsMain();
Clase.Make_Diff_Breve_T_S(ref Datos,  1);
Clase.Persist_DF_Json(ref Datos, "/home/jr-fuentes-martinez/Documentos/DiffRules/persist.json");
//var Grafico = Clase.Make_Plotly_3DGraph(ref LosDatos, 0, 0);
//Grafico.Show();

