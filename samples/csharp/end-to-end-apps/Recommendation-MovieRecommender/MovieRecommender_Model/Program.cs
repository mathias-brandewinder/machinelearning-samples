﻿using System;
using Microsoft.ML;
using System.IO;
using System.Linq;
using Microsoft.ML.Data;
using Console = Colorful.Console;
using System.Drawing;
using Microsoft.ML.Core.Data;

namespace MovieRecommenderModel
{
    /* This movie recommendation model is built on the http://files.grouplens.org/datasets/movielens/ml-latest-small.zip dataset
       for improved model performance use the https://grouplens.org/datasets/movielens/1m/ dataset instead. */

    class Program
    {
        private static string TrainingDataLocation = @".\Data\ratings_train.csv";
        private static string TestDataLocation = @".\Data\ratings_test.csv";
        private static string ModelPath = @"..\..\..\Model\model.zip";

        private static string userId = nameof(userId);
        private static string userIdFeaturized = nameof(userIdFeaturized);

        private static string movieId = nameof(movieId);
        private static string movieIdFeaturized = nameof(movieIdFeaturized);

        private static string Label = nameof(Label);
        private static string Features = nameof(Features);

        private static string Score = nameof(Score);

        static void Main(string[] args)
        {
            Color color = Color.FromArgb(130,150,115);

            //Call the following piece of code for splitting the ratings.csv into ratings_train.csv and ratings.test.csv.
            // Program.DataPrep();

            //STEP 1: Create MLContext to be shared across the model creation workflow objects
            MLContext mlContext = new MLContext();

            //STEP 2: Create a TextLoader by defining the schema for reading the movie recommendation datasets
            var reader = mlContext.Data.CreateTextReader(new TextLoader.Arguments()
            {
                Separator = ",",
                HasHeader = true,
                Column = new[]
                {
                    new TextLoader.Column("userId", DataKind.Text, 0),
                    new TextLoader.Column("movieId", DataKind.Text, 1),
                    new TextLoader.Column("Label", DataKind.BL, 2)
                }
            });

            Console.WriteLine("=============== Reading Input Files ===============", color);
            Console.WriteLine();

            //STEP 3: Read the training data and test data which will be used to train and test the movie recommendation model
            var trainingDataView = reader.Read(TrainingDataLocation);

            // ML.NET doesn't cache data set by default. Therefore, if one reads a data set from a file and accesses it many times, it can be slow due to
            // expensive featurization and disk operations. When the considered data can fit into memory, a solution is to cache the data in memory. Caching is especially
            // helpful when working with iterative algorithms which needs many data passes. Since SDCA is the case, we cache. Inserting a
            // cache step in a pipeline is also possible, please see the construction of pipeline below.
            trainingDataView = mlContext.Data.Cache(trainingDataView);

            Console.WriteLine("=============== Transform Data And Preview ===============", color);
            Console.WriteLine();

            //STEP 4: Transform your data by encoding the two features userId and movieID.
            //        These encoded features will be provided as input to FieldAwareFactorizationMachine learner


            var pipeline = mlContext.Transforms.Text.FeaturizeText(userId, userIdFeaturized)
                                          .Append(mlContext.Transforms.Text.FeaturizeText(movieId, movieIdFeaturized)
                                          .Append(mlContext.Transforms.Concatenate(Features, userIdFeaturized, movieIdFeaturized))
                                          //.AppendCacheCheckpoint(mlContext) // Add a data-cache step within a pipeline.
                                          .Append(mlContext.BinaryClassification.Trainers.FieldAwareFactorizationMachine(new string[] { Features })));

            var preview = pipeline.Preview(trainingDataView, maxRows: 10);

            // STEP 5: Train the model fitting to the DataSet
            Console.WriteLine("=============== Training the model ===============", color);
            Console.WriteLine();

            var model = pipeline.Fit(trainingDataView);

            //STEP 6: Evaluate the model performance
            Console.WriteLine("=============== Evaluating the model ===============", color);
            Console.WriteLine();
            var testDataView = reader.Read(TestDataLocation);
            var prediction = model.Transform(testDataView);

            var metrics = mlContext.BinaryClassification.Evaluate(prediction, label: "Label", score: "Score", predictedLabel: "PredictedLabel");
            Console.WriteLine("Evaluation Metrics: acc:" + Math.Round(metrics.Accuracy, 2) + " auc:" + Math.Round(metrics.Auc, 2),color);

            //STEP 7:  Try/test a single prediction by predicting a single movie rating for a specific user
            Console.WriteLine("=============== Test a single prediction ===============", color);
            Console.WriteLine();
            var predictionEngine = model.CreatePredictionEngine<MovieRating, MovieRatingPrediction>(mlContext);
            MovieRating testData = new MovieRating() { userId = "6", movieId = "10" };

            var movieRatingPrediction = predictionEngine.Predict(testData);
            Console.WriteLine($"UserId:{testData.userId} with movieId: {testData.movieId} Score:{Sigmoid(movieRatingPrediction.Score)} and Label {movieRatingPrediction.PredictedLabel}", Color.YellowGreen);
            Console.WriteLine();

            //STEP 8:  Save model to disk
            Console.WriteLine("=============== Writing model to the disk ===============", color);
            Console.WriteLine();

            using (FileStream fs = new FileStream(ModelPath, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                mlContext.Model.Save(model, fs);
            }

            Console.WriteLine("=============== Re-Loading model from the disk ===============", color);
            Console.WriteLine();
            ITransformer trainedModel;
            using (FileStream stream = new FileStream(ModelPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                trainedModel = mlContext.Model.Load(stream);
            }

            Console.WriteLine("Press any key ...");
            Console.Read();
        }

        /*
         * FieldAwareFactorizationMachine the learner used in this example requires the problem to setup as a binary classification problem.
         * The DataPrep method performs two tasks:
         * 1. It goes through all the ratings and replaces the ratings > 3 as 1, suggesting a movie is recommended and ratings < 3 as 0, suggesting
              a movie is not recommended
           2. This piece of code also splits the ratings.csv into rating-train.csv and ratings-test.csv used for model training and testing respectively.
         */
        public static void DataPrep()
        {

            string[] dataset = File.ReadAllLines(@".\Data\ratings.csv");

            string[] new_dataset = new string[dataset.Length];
            new_dataset[0] = dataset[0];
            for (int i = 1; i < dataset.Length; i++)
            {
                string line = dataset[i];
                string[] lineSplit = line.Split(',');
                double rating = Double.Parse(lineSplit[2]);
                rating = rating > 3 ? 1 : 0;
                lineSplit[2] = rating.ToString();
                string new_line = string.Join(',', lineSplit);
                new_dataset[i] = new_line;
            }
            dataset = new_dataset;
            int numLines = dataset.Length;
            var body = dataset.Skip(1);
            var sorted = body.Select(line => new { SortKey = Int32.Parse(line.Split(',')[3]), Line = line })
                             .OrderBy(x => x.SortKey)
                             .Select(x => x.Line);
            File.WriteAllLines(@"..\..\..\Data\ratings_train.csv", dataset.Take(1).Concat(sorted.Take((int)(numLines * 0.9))));
            File.WriteAllLines(@"..\..\..\Data\ratings_test.csv", dataset.Take(1).Concat(sorted.TakeLast((int)(numLines * 0.1))));
        }

        public static float Sigmoid(float x)
        {
            return (float)(100 / (1 + Math.Exp(-x)));
        }
    }


    public class MovieRating
    {
        public string userId;

        public string movieId;

        public bool Label;
    }

    public class MovieRatingPrediction
    {
        public bool PredictedLabel;

        public float Score;
    }
}
