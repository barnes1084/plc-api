namespace plc_api.Models.Tags
{
    using Swashbuckle.AspNetCore.Annotations;

    public enum DriverType { EIP, ModbusTCP }
    public enum PlcDataType { DINT, BOOL }
    public enum TagMode { Dashboard, Trend, Command }

    [SwaggerSchema(
        Description = "Defines a PLC tag used for dashboard polling, trending, or command access"
    )]
    public record TagDefinition(
        string Id,
        string Name,
        DriverType DriverType,
        string Ip,
        string Path,
        string Address,
        PlcDataType DataType,
        TagMode Mode,
        int PollMs = 2500,
        bool Enabled = true
    );

}