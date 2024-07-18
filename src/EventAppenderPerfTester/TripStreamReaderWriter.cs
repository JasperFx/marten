using DaemonTests.TestingSupport;
using JasperFx.Core;
using Newtonsoft.Json;

namespace EventAppenderPerfTester;

public static class TripStreamReaderWriter
{
    private static JsonSerializer _serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Auto
    });

    public static string Path { get; } = AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory()
        .AppendPath("data.json");

    public static async Task Write(TripStream[] trips)
    {
        var all = trips.Select(x => x.Events).ToArray();

        await using var stream = new FileStream(Path, FileMode.Create);
        var writer = new StreamWriter(stream);
        _serializer.Serialize(writer, all);
        await writer.FlushAsync();
    }

    public static TripStream[] Read()
    {
        using var stream = new FileStream(Path, FileMode.Open);
        var reader = new StreamReader(stream);
        object[][] raw = _serializer.Deserialize<object[][]>(new JsonTextReader(reader));

        return raw.Select(x => new TripStream(new List<object>(x))).ToArray();
    }

    public static List<TripStream> ReadPages(int pageSize)
    {
        return Enumerable.Range(0, pageSize).SelectMany(x => Read()).ToList();
    }

    public static List<TripStream[]> ReadPages(int pageTotal, int pageSize)
    {
        var list = new List<TripStream[]>();
        var trips = new Queue<TripStream>(Read());

        for (int i = 0; i < pageTotal; i++)
        {
            var page = new TripStream[pageSize];
            for (int j = 0; j < pageSize; j++)
            {
                if (trips.TryDequeue(out var result))
                {
                    page[j] = result;
                }
                else
                {
                    trips = new Queue<TripStream>(Read());
                    page[j] = trips.Dequeue();
                }
            }

            list.Add(page);
        }

        return list;
    }
}