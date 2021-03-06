﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DlxLib;
using System.IO;
using Microsoft.Spark.Sql;

namespace ESGF.Sudoku.Spark.Dancinlinks
{
    public static class Program{
        //Path du fichier csv avec 1 000 000 sudokus.
        static readonly string _filePath = Path.Combine("/Users/yassine/Documents/GitHub/5ESGF-BD-2021/ESGF.Sudoku.Spark.Dancinlinks/", "sudoku.csv");

        public static void Main(){
            //temps d'execution global (chargement du CSV + création DF et sparksession)
            var watch = new System.Diagnostics.Stopwatch();
            var watch1 = new System.Diagnostics.Stopwatch();

            //watch.Start();

            ////Appel de la méthode, spark session avec 1 noyau et 1 instance, 1000 sudokus à résoudre
            //Sudokures("1", "1", "512M", 1000);

            //watch.Stop();


            watch.Start();

            //Appel de la méthode, spark session avec 1 noyau et 1 instance, 1000 sudokus à résoudre
            Sudokures(1000);

            watch.Stop();



            //watch1.Start();

            ////Appel de la méthode, spark session avec 1 noyau et 4 instance, 1000 sudokus à résoudre
            //Sudokures("8", "24", "4G", 1000);

            //watch1.Stop();

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"Global Execution (CSV + DF + SparkSession) Time: {watch.ElapsedMilliseconds} ms");
            //Console.WriteLine($"Global Execution (CSV + DF + SparkSession) Time with 4 core and 12 instances: {watch1.ElapsedMilliseconds} ms");
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();

        }

        //Méthode qui est appelée depuis le main pour lancer une session spark avec un nombbre de noyaux et d'instances différents et lancer la résolution du soduku grace à la méthode Sudokusolution().
        //private static void Sudokures(string cores, string nodes, string mem, int nrows){
        private static void Sudokures(int nrows){
                // Initialisation de la session Spark
                SparkSession spark = SparkSession
                .Builder()
                .Config("spark.executor.memory", "4G")
                .GetOrCreate();
            //.AppName("Resolution of " + nrows + " sudokus using DlxLib with " + cores + " cores and " + nodes + " instances")
            //.Config("spark.driver.cores", cores)
            //.Config("spark.executor.instances", nodes)
            //.Config("spark.executor.memory", mem)
            //.GetOrCreate();

            // Intégration du csv dans un dataframe
            DataFrame df = spark
                .Read()
                .Option("header", true)
                .Option("inferSchema", true)
                .Csv(_filePath);

            //limit du dataframe avec un nombre de ligne prédéfini lors de l'appel de la fonction
            DataFrame df2 = df.Limit(nrows);

            //Watch seulement pour la résolution des sudokus
            var watch2 = new System.Diagnostics.Stopwatch();
            watch2.Start();

            // Création de la spark User Defined Function
            spark.Udf().Register<string, string>(
                "SukoduUDF",
                (sudoku) => Sudokusolution(sudoku));

            // Appel de l'UDF dans un nouveau dataframe spark qui contiendra les résultats aussi
            df2.CreateOrReplaceTempView("Resolved");
            DataFrame sqlDf = spark.Sql("SELECT Sudokus, SukoduUDF(Sudokus) as Resolution from Resolved");
            sqlDf.Show();

            watch2.Stop();

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"Execution Time for " + nrows + " sudoku resolution : " + watch2.ElapsedMilliseconds + " ms");
            //Console.WriteLine($"Execution Time for " + nrows + " sudoku resolution with " + cores + " core and " + nodes + " instance: " + watch2.ElapsedMilliseconds + " ms");
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();

            spark.Stop();

        }

        //Création d'une méthode qui prend en entrée un string (le sudoku non résolu) et qui renvoie un string (le sudoku résolu).
        //De base le code duquel on s'est inspiré ne renvoyait rien, la sortie était sous forme d'un Console.WriteLine()
        private static string Sudokusolution(string sudoku){

                //Récupération du sudoku à résoudre depuis le string en entrée et transfert dans une ImmutableList.
                var grid = new Grid(ImmutableList.Create(
                sudoku.Substring(0, 9),
                sudoku.Substring(9, 9),
                sudoku.Substring(18, 9),
                sudoku.Substring(27, 9),
                sudoku.Substring(36, 9),
                sudoku.Substring(45, 9),
                sudoku.Substring(54, 9),
                sudoku.Substring(63, 9),
                sudoku.Substring(72, 9)));

                var internalRows = BuildInternalRowsForGrid(grid);
                var dlxRows = BuildDlxRows(internalRows);

                var solutions = new Dlx()
                    .Solve(BuildDlxRows(internalRows), d => d, r => r)
                    .Where(solution => VerifySolution(internalRows, solution))
                    .ToImmutableList();

                if (solutions.Any()){
                    //Enlever commentaire pour afficher les solutions dans la console

                    //Console.WriteLine($"First solution (of {solutions.Count}):");
                    //Console.WriteLine();
                    //SolutionToGrid(internalRows, solutions.First()).Draw();
                    //Console.WriteLine();

                    //Ajout de ce bout de code pour avoir une sortie de type string contenant le sudoku résolu.
                    string s = "";
                    for (int i = 0; i <= 8; i++){
                        for (int j = 0; j <= 8; j++){
                            s += SolutionToGrid(internalRows, solutions.First()).ValueAt(i, j).ToString();
                        }
                    }

                    return s;
                } else {
                    //Console.WriteLine("No solutions found!");
                    return "No solutions found!";
                }
        
        }


        //Code non modifié pris sur le repo de résolution des sudoku avec dancinlinks
        private static IEnumerable<int> Rows => Enumerable.Range(0, 9);
        private static IEnumerable<int> Cols => Enumerable.Range(0, 9);
        private static IEnumerable<Tuple<int, int>> Locations =>
            from row in Rows
            from col in Cols
            select Tuple.Create(row, col);
        private static IEnumerable<int> Digits => Enumerable.Range(1, 9);

        private static IImmutableList<Tuple<int, int, int, bool>> BuildInternalRowsForGrid(Grid grid)
        {
            var rowsByCols =
                from row in Rows
                from col in Cols
                let value = grid.ValueAt(row, col)
                select BuildInternalRowsForCell(row, col, value);

            return rowsByCols.SelectMany(cols => cols).ToImmutableList();
        }

        private static IImmutableList<Tuple<int, int, int, bool>> BuildInternalRowsForCell(int row, int col, int value)
        {
            if (value >= 1 && value <= 9)
                return ImmutableList.Create(Tuple.Create(row, col, value, true));

            return Digits.Select(v => Tuple.Create(row, col, v, false)).ToImmutableList();
        }

        private static IImmutableList<IImmutableList<int>> BuildDlxRows(
            IEnumerable<Tuple<int, int, int, bool>> internalRows)
        {
            return internalRows.Select(BuildDlxRow).ToImmutableList();
        }

        private static IImmutableList<int> BuildDlxRow(Tuple<int, int, int, bool> internalRow)
        {
            var row = internalRow.Item1;
            var col = internalRow.Item2;
            var value = internalRow.Item3;
            var box = RowColToBox(row, col);

            var posVals = Encode(row, col);
            var rowVals = Encode(row, value - 1);
            var colVals = Encode(col, value - 1);
            var boxVals = Encode(box, value - 1);

            return posVals.Concat(rowVals).Concat(colVals).Concat(boxVals).ToImmutableList();
        }

        private static int RowColToBox(int row, int col)
        {
            return row - (row % 3) + (col / 3);
        }

        private static IEnumerable<int> Encode(int major, int minor)
        {
            var result = new int[81];
            result[major * 9 + minor] = 1;
            return result.ToImmutableList();
        }

        private static bool VerifySolution(
            IReadOnlyList<Tuple<int, int, int, bool>> internalRows,
            Solution solution)
        {
            var solutionInternalRows = solution.RowIndexes
                .Select(rowIndex => internalRows[rowIndex])
                .ToImmutableList();

            var locationsGroupedByRow = Locations.GroupBy(t => t.Item1);
            var locationsGroupedByCol = Locations.GroupBy(t => t.Item2);
            var locationsGroupedByBox = Locations.GroupBy(t => RowColToBox(t.Item1, t.Item2));

            return
                CheckGroupsOfLocations(solutionInternalRows, locationsGroupedByRow, "row") &&
                CheckGroupsOfLocations(solutionInternalRows, locationsGroupedByCol, "col") &&
                CheckGroupsOfLocations(solutionInternalRows, locationsGroupedByBox, "box");
        }

        private static bool CheckGroupsOfLocations(
            IEnumerable<Tuple<int, int, int, bool>> solutionInternalRows,
            IEnumerable<IGrouping<int, Tuple<int, int>>> groupedLocations,
            string tag)
        {
            return groupedLocations.All(grouping =>
                CheckLocations(solutionInternalRows, grouping, grouping.Key, tag));
        }

        private static bool CheckLocations(
            IEnumerable<Tuple<int, int, int, bool>> solutionInternalRows,
            IEnumerable<Tuple<int, int>> locations,
            int key,
            string tag)
        {
            var digits = locations.SelectMany(location =>
                solutionInternalRows
                    .Where(solutionInternalRow =>
                        solutionInternalRow.Item1 == location.Item1 &&
                        solutionInternalRow.Item2 == location.Item2)
                    .Select(t => t.Item3));
            return CheckDigits(digits, key, tag);
        }

        private static bool CheckDigits(
            IEnumerable<int> digits,
            int key,
            string tag)
        {
            var actual = digits.OrderBy(v => v);
            if (actual.SequenceEqual(Digits)) return true;
            var values = string.Concat(actual.Select(n => Convert.ToString(n)));
            Console.WriteLine($"{tag} {key}: {values} !!!");
            return false;
        }

        private static Grid SolutionToGrid(
            IReadOnlyList<Tuple<int, int, int, bool>> internalRows,
            Solution solution)
        {
            var rowStrings = solution.RowIndexes
                .Select(rowIndex => internalRows[rowIndex])
                .OrderBy(t => t.Item1)
                .ThenBy(t => t.Item2)
                .GroupBy(t => t.Item1, t => t.Item3)
                .Select(value => string.Concat(value))
                .ToImmutableList();
            return new Grid(rowStrings);
        }

    }
}
