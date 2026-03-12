namespace plc_api.Drivers.ModbusTCP
{
    public class ModbusTCP : IDisposable
    {
        public ModbusTCP(string ip, string path)
        {
            Ip = ip;
            Path = path;
        }

        public string Ip { get; }
        public string Path { get; }
        public bool isConnected { get; internal set; }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        internal bool ReadBool(string address)
        {
            throw new NotImplementedException();
        }

        internal int ReadDint(string address)
        {
            throw new NotImplementedException();
        }

        internal bool WriteBool(string address, bool value)
        {
            throw new NotImplementedException();
        }

        internal bool WriteDint(string address, int value)
        {
            throw new NotImplementedException();
        }
    }
}
