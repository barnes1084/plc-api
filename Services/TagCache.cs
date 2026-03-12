using plc_api.Models.Tags;
using StackExchange.Redis;

namespace plc_api.Services
{
    public class TagCache
    {
        private readonly IDatabase _db;
        public TagCache(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

        private static string Key(string id) => $"plc:latest:{id}";

        public Task SetLatestAsync(string id, string value, long ts, string quality) =>
            _db.HashSetAsync(Key(id), new HashEntry[]
            {
            new("value", value),
            new("ts", ts),
            new("quality", quality),
            });

        public async Task<TagLatest?> GetLatestAsync(string id)
        {
            var hash = await _db.HashGetAllAsync(Key(id));
            if (hash.Length == 0) return null;

            string value = hash.FirstOrDefault(x => x.Name == "value").Value!;
            string tsStr = hash.FirstOrDefault(x => x.Name == "ts").Value!;
            string quality = hash.FirstOrDefault(x => x.Name == "quality").Value!;

            long ts = long.TryParse(tsStr, out var v) ? v : 0;
            return new TagLatest(id, value, ts, quality);
        }

        public Task DeleteLatestAsync(string id)
            => _db.KeyDeleteAsync(Key(id));
    }
}
