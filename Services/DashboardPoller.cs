using plc_api.Models.Tags;

namespace plc_api.Services
{
    public class DashboardPoller : BackgroundService
    {
        private readonly TagRegistry _tags;
        private readonly TagCache _cache;
        private readonly PlcConnectionManager _plcMgr;

        public DashboardPoller(TagRegistry tags, TagCache cache, PlcConnectionManager plcMgr)
        {
            _tags = tags;
            _cache = cache;
            _plcMgr = plcMgr;
        }




        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("DashboardPoller started");
            var lastRun = new Dictionary<string, long>();

            while (!stoppingToken.IsCancellationRequested)
            {
                var all = await _tags.GetAllAsync();
                var dashboardTags = all.Where(t => t.Enabled && t.Mode == TagMode.Dashboard).ToList();

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                foreach (var group in dashboardTags.GroupBy(t => (t.DriverType, t.Ip, t.Path, t.PollMs)))
                {
                    string groupKey = $"{group.Key.DriverType}|{group.Key.Ip}|{group.Key.Path}|{group.Key.PollMs}";
                    lastRun.TryGetValue(groupKey, out long last);
                    if (now - last < group.Key.PollMs) continue;

                    lastRun[groupKey] = now;

                    await _plcMgr.UseAsync(group.Key.DriverType, group.Key.Ip, group.Key.Path, async plc =>
                    {
                        foreach (var tag in group)
                        {
                            try
                            {
                                string val = tag.DataType switch
                                {
                                    PlcDataType.DINT => plc.ReadDint(tag.Address).ToString(),
                                    PlcDataType.BOOL => plc.ReadBool(tag.Address).ToString(),
                                    _ => ""
                                };

                                await _cache.SetLatestAsync(tag.Id, val, now, "good");
                            }
                            catch
                            {
                                await _cache.SetLatestAsync(tag.Id, "", now, "bad");
                            }
                        }
                        return true;
                    });
                }

                await Task.Delay(100, stoppingToken);
            }
        }
    }
}

