using System;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;

class Program
{
    static async Task Main()
    {
        // Connection string comes from the MONGODB_CONNECTION_STRING environment variable.
        // e.g. (PowerShell):  $env:MONGODB_CONNECTION_STRING = "mongodb+srv://user:pass@cluster.mongodb.net/"
        var connectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Set the MONGODB_CONNECTION_STRING environment variable first.");
            return;
        }

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase("distributed_rag");
        
        Console.WriteLine("=== TASKS ===");
        var tasksCollection = database.GetCollection<BsonDocument>("backgroundTasks");
        var tasks = await tasksCollection.Find(new BsonDocument()).SortByDescending(t => t["UpdatedAt"]).Limit(3).ToListAsync();
        foreach (var task in tasks)
        {
            Console.WriteLine(task.ToJson());
        }

        Console.WriteLine("\n=== CHUNKS ===");
        var chunksCollection = database.GetCollection<BsonDocument>("embeddings");
        var chunksCount = await chunksCollection.CountDocumentsAsync(new BsonDocument());
        Console.WriteLine($"Total chunks in database: {chunksCount}");
        
        var chunks = await chunksCollection.Find(new BsonDocument()).Limit(1).ToListAsync();
        foreach (var chunk in chunks)
        {
            Console.WriteLine(chunk.ToJson());
        }
    }
}
