using plc_api.Models.Json;
using plc_api.Models.Tags;
using StackExchange.Redis;
using System.Text.Json;

namespace plc_api.Services
{
    public class TagRegistry
    {
        private readonly IDatabase _db;
        private const string TagIndexKey = "plc:tags";

        public TagRegistry(IConnectionMultiplexer mux) => _db = mux.GetDatabase();



        // using Redis as a in memory storage,
        // CRUD: add/update/delete tags, enable/disable tags
        public async Task AddOrUpdateAsync(TagDefinition tag)
        {
            string json = JsonSerializer.Serialize(tag, JsonDefaults.Options);
            await _db.HashSetAsync(TagIndexKey, tag.Id, json);
        }

        public async Task<TagDefinition?> GetAsync(string id)
        {
            var val = await _db.HashGetAsync(TagIndexKey, id);
            return val.IsNullOrEmpty ? null : JsonSerializer.Deserialize<TagDefinition>(val!, JsonDefaults.Options);
        }

        public async Task<List<TagDefinition>> GetAllAsync()
        {
            var entries = await _db.HashGetAllAsync(TagIndexKey);
            var list = new List<TagDefinition>(entries.Length);
            foreach (var e in entries)
            {
                var t = JsonSerializer.Deserialize<TagDefinition>(e.Value!, JsonDefaults.Options);
                if (t != null) list.Add(t);
            }
            return list;
        }

        public Task DeleteAsync(string id) => _db.HashDeleteAsync(TagIndexKey, id);
    }
}
