using plc_api.Models.Tags;
using plc_api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace plc_api.Controllers
{
    [ApiController]
    [Route("api/plc")]
    public class PlcController : ControllerBase
    {
        private readonly TagRegistry _registry;
        private readonly TagCache _cache;
        private readonly PlcConnectionManager _plcMgr;

        public PlcController(TagRegistry registry, TagCache cache, PlcConnectionManager plcMgr)
        {
            _registry = registry;
            _cache = cache;
            _plcMgr = plcMgr;
        }


        // Endpoints ======================================================================================


        [HttpPost("tags")]
        public async Task<IActionResult> UpsertTags([FromBody] List<TagDefinition> tags)
        {
            if (tags == null || tags.Count == 0)
                return BadRequest("Provide at least one tag.");

            var results = new List<object>(tags.Count);

            foreach (var tag in tags)
            {
                var id = string.IsNullOrWhiteSpace(tag.Id)
                    ? Guid.NewGuid().ToString("N")
                    : tag.Id;

                await _registry.AddOrUpdateAsync(tag with { Id = id });

                results.Add(new
                {
                    id,
                    name = tag.Name
                });
            }

            return Ok(new
            {
                upserted = results.Count,
                tags = results
            });
        }



        [HttpGet("tags")]
        public async Task<List<TagDefinition>> GetTags()
        {
            var tags = await _registry.GetAllAsync();
            return tags
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        [HttpDelete("tags/{id}")]
        public async Task<IActionResult> DeleteTag(string id)
        {
            await _registry.DeleteAsync(id);
            await _cache.DeleteLatestAsync(id);
            return Ok();
        }

        [HttpGet("dashboard/latest")]
        public async Task<IActionResult> GetLatest([FromQuery] string[] ids)
        {
            var tasks = ids.Select(id => _cache.GetLatestAsync(id));
            var results = await Task.WhenAll(tasks);
            return Ok(results.Where(v => v != null));
        }

        [HttpPost("command/read/{id}")]
        public async Task<IActionResult> CommandRead(string id)
        {
            var tag = await _registry.GetAsync(id);
            if (tag == null) return NotFound();

            if (tag.Mode != Models.Tags.TagMode.Command) return BadRequest("Tag is not Command mode.");

            var value = await _plcMgr.UseAsync(tag, driver =>
            {
                object? v = tag.DataType switch
                {
                    PlcDataType.DINT => driver.ReadDint(tag.Address),
                    PlcDataType.BOOL => driver.ReadBool(tag.Address),
                    _ => null
                };
                return Task.FromResult(v);
            });

            return Ok(value);
        }



        public record WriteRequest(string Value);

        [HttpPost("command/write/{id}")]
        public async Task<IActionResult> CommandWrite(string id, [FromBody] WriteRequest req)
        {
            var tag = await _registry.GetAsync(id);
            if (tag == null) return NotFound();

            if (tag.Mode != Models.Tags.TagMode.Command)
                return BadRequest("Tag is not Command mode.");

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var result = await _plcMgr.UseAsync(tag, driver =>
            {
                bool writeOk;
                object? readValue = null;

                // Write
                writeOk = tag.DataType switch
                {
                    PlcDataType.DINT => int.TryParse(req.Value, out var i) && driver.WriteDint(tag.Address, i),
                    PlcDataType.BOOL => bool.TryParse(req.Value, out var b) && driver.WriteBool(tag.Address, b),
                    _ => false
                };

                if (!writeOk)
                    return Task.FromResult((ok: false, value: (object?)null));

                // Read back
                readValue = tag.DataType switch
                {
                    PlcDataType.DINT => driver.ReadDint(tag.Address),
                    PlcDataType.BOOL => driver.ReadBool(tag.Address),
                    _ => null
                };

                return Task.FromResult((ok: true, value: readValue));
            });

            // Update Redis
            if (result.ok && result.value != null)
            {
                await _cache.SetLatestAsync(
                    tag.Id,
                    result.value.ToString()!,
                    now,
                    "good"
                );
            }
            else
            {
                await _cache.SetLatestAsync(
                    tag.Id,
                    "",
                    now,
                    "bad"
                );
            }

            // Return response
            return Ok(new
            {
                ok = result.ok,
                value = result.value
            });
        }

    }
}