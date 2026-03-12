using plc_api.Drivers.EIP;
using plc_api.Drivers.ModbusTCP;
using plc_api.Drivers;
using plc_api.Models.Tags;
using System.Collections.Concurrent;

namespace plc_api.Services
{
    public class PlcConnectionManager
    {
        private class Conn
        {
            public IPlcDriver Plc { get; set; }
            public SemaphoreSlim Gate { get; } = new(1, 1);
            public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

            public Conn(IPlcDriver plc) => Plc = plc;
        }

        private readonly ConcurrentDictionary<string, Conn> _conns = new();

        private static string Key(DriverType driverType, string ip, string path) => $"{driverType}|{ip}|{path}";

        private static IPlcDriver Create(DriverType driverType, string ip, string path)
        {
            return driverType switch
            {
                DriverType.EIP => new EipPlcDriver(ip, path),
                DriverType.ModbusTCP => new ModbusTcpDriver(ip, path),
                _ => throw new NotSupportedException($"DriverType not supported: {driverType}")
            };
        }

        public Task<T> UseAsync<T>(TagDefinition tag, Func<IPlcDriver, Task<T>> action) => UseAsync(tag.DriverType, tag.Ip, tag.Path, action);



        public async Task<T> UseAsync<T>(
            DriverType driverType,
            string ip, string path,
            Func<IPlcDriver,
            Task<T>> action)
        {
            var key = Key(driverType, ip, path);
            var conn = _conns.GetOrAdd(key, _ => new Conn(Create(driverType, ip, path)));

            await conn.Gate.WaitAsync();
            try
            {
                conn.LastUsedUtc = DateTime.UtcNow;

                if (!conn.Plc.IsConnected)
                {
                    conn.Plc.Dispose();
                    conn.Plc = Create(driverType, ip, path);
                }

                return await action(conn.Plc);
            }
            finally
            {
                conn.Gate.Release();
            }
        }
    }
}

