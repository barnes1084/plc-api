namespace plc_api.Drivers
{
    public interface IPlcDriver : IDisposable
    {
        bool IsConnected { get; }
        int ReadDint(string address);
        bool WriteDint(string address, int value);
        bool ReadBool(string address);
        bool WriteBool(string address, bool value);
    }
}
