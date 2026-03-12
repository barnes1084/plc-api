namespace plc_api.Drivers.EIP
{
    public sealed class EipPlcDriver : IPlcDriver
    {
        private readonly EIP _eip;

        public EipPlcDriver(string ip, string path) => _eip = new EIP(ip, path);

        public bool IsConnected => _eip.isConnected;

        public int ReadDint(string address) => _eip.ReadDint(address);
        public bool WriteDint(string address, int value) => _eip.WriteDint(address, value);
        public bool ReadBool(string address) => _eip.ReadBool(address);
        public bool WriteBool(string address, bool value) => _eip.WriteBool(address, value);

        public void Dispose() => _eip.Dispose();
    }
}
