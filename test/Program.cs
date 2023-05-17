using DiffRulesLib;

// See https://aka.ms/new-console-template for more information

double[] Numeros = new double[400];
for (int i = 0; i < Numeros.Length; i++) {
    Numeros[i] = 5.0 * Math.Pow(i, 2);    
}
List<double[]> Lista = new() {
    Numeros, Numeros, Numeros
};
List<List<double[]>> Todas = new() {Lista, Lista, Lista};

double[] LaSerie = new double[] {1, 2, 3};
DataFrame LosDatos = new DataFrame() {
    Clave1Double = 0.0,
    Clave2Double = 0.0,
    Clave3DateTime = DateTime.Now,
    Serie = LaSerie,
    ColumnasIn = Todas,
    Coletilla = string.Empty
};
var Clase = new ClsMain();
Clase.Make_Diff_Breve_T_S(ref LosDatos,  1);
Clase.Persist_DF_Json(ref LosDatos, "/home/jr-fuentes-martinez/Documentos/DiffRules/persist.html");

var a = 1;

