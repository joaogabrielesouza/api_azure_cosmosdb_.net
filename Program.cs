using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Azure.Cosmos.Samples.Bulk
{
    public class Program
    {
        private const string EndpointUrl = "https://localhost:8081";
        private const string AuthorizationKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private const string DatabaseName = "bulk";
        private const string ContainerName = "items";
        private const int AmountToInsert = 300000;

        static async Task Main(string[] args)
        {
            CosmosClient cosmosClient = new CosmosClient(EndpointUrl, AuthorizationKey, new CosmosClientOptions() { AllowBulkExecution = true });

            Console.WriteLine("This tutorial will create a 50000 RU/s container, press any key to continue.");
            Console.ReadKey();

            Database database = await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseName);

            await database.DefineContainer(Program.ContainerName, "/pk")
                    .WithIndexingPolicy()
                        .WithIndexingMode(IndexingMode.Consistent)
                        .WithIncludedPaths()
                            .Attach()
                        .WithExcludedPaths()
                            .Path("/*")
                            .Attach()
                    .Attach()
                .CreateAsync(50000);

            try
            {
                Console.WriteLine($"Preparing {AmountToInsert} items to insert...");

                List<Item> itemsToInsert;
                using (var reader = new StreamReader("csvfile.csv"))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = ","
                }))
                {
                    itemsToInsert = csv.GetRecords<Item>().ToList();
                }

                Console.WriteLine("Starting...");
                Stopwatch stopwatch = Stopwatch.StartNew();
                Container container = database.GetContainer(ContainerName);
                List<Task> tasks = new List<Task>(AmountToInsert);

                foreach (Item item in itemsToInsert)
                {
                    tasks.Add(container.CreateItemAsync(item, new PartitionKey(item.pk))
                        .ContinueWith(itemResponse =>
                        {
                            if (!itemResponse.IsCompletedSuccessfully)
                            {
                                AggregateException innerExceptions = itemResponse.Exception.Flatten();
                                if (innerExceptions.InnerExceptions.FirstOrDefault(innerEx => innerEx is CosmosException) is CosmosException cosmosException)
                                {
                                    Console.WriteLine($"Received {cosmosException.StatusCode} ({cosmosException.Message}).");
                                }
                                else
                                {
                                    Console.WriteLine($"Exception {innerExceptions.InnerExceptions.FirstOrDefault()}.");
                                }
                            }
                        }));
                }

                await Task.WhenAll(tasks);
                stopwatch.Stop();

                Console.WriteLine($"Finished in writing {AmountToInsert} items in {stopwatch.Elapsed}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                Console.WriteLine("UPSERT FINISHED");
            }
        }
        public class Item
        {
 
            public string id { get; set; }
            public string pk { get; set; }

        }
    }
}