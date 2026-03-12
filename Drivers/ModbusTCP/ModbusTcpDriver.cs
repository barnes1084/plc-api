namespace plc_api.Drivers.ModbusTCP
{
    public sealed class ModbusTcpDriver : IPlcDriver
    {
        private readonly ModbusTCP __modbusTcp;

        public ModbusTcpDriver(string ip, string path) => __modbusTcp = new ModbusTCP(ip, path);

        public bool IsConnected => __modbusTcp.isConnected;

        public int ReadDint(string address) => __modbusTcp.ReadDint(address);
        public bool WriteDint(string address, int value) => __modbusTcp.WriteDint(address, value);
        public bool ReadBool(string address) => __modbusTcp.ReadBool(address);
        public bool WriteBool(string address, bool value) => __modbusTcp.WriteBool(address, value);

        public void Dispose() => __modbusTcp.Dispose();
    }
}

