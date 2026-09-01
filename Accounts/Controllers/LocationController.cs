using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace Accounts.Controllers
{

    [ApiController]
    [Route("api/locations")]
    [Produces("application/json")]
    public class LocationController : ControllerBase
    {
        private readonly IHttpClientFactory _httpFactory;

        public LocationController(IHttpClientFactory httpFactory)
        {
            _httpFactory = httpFactory;
        }

        private static IReadOnlyList<object> GetLocalCountries() =>
            CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Select(c =>
                {
                    try { return new RegionInfo(c.Name); }
                    catch (ArgumentException) { return null; }
                })
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.EnglishName))
                .GroupBy(r => r!.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()!)
                .OrderBy(r => r.EnglishName)
                .Select(r => (object)new
                {
                    name = r.EnglishName,
                    code = r.TwoLetterISORegionName,
                    flagUrl = $"https://flagcdn.com/{r.TwoLetterISORegionName.ToLowerInvariant()}.svg"
                })
                .ToList();

        [HttpGet("countries")]
        public async Task<IActionResult> GetCountries()
        {
            try
            {
                var client = _httpFactory.CreateClient("CountryApi");
                var resp = await client.GetAsync("all?fields=name,cca2,flags");

                if (!resp.IsSuccessStatusCode)
                    return Ok(GetLocalCountries());

                var json = await resp.Content.ReadAsStringAsync();

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var rawArray = new List<JsonElement>();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    rawArray = root.EnumerateArray().ToList();
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    rawArray.Add(root);
                }

                if (!rawArray.Any()) return Ok(GetLocalCountries());

                var countries = rawArray
                    .Select(c => new
                    {
                        name = c.GetProperty("name").GetProperty("common").GetString(),
                        code = c.TryGetProperty("cca2", out var cca2) ? cca2.GetString() : null,
                        flagUrl = c.TryGetProperty("flags", out var f)
                                  ? f.TryGetProperty("svg", out var svg) ? svg.GetString() : null
                                  : null
                    })
                    .Where(c => !string.IsNullOrEmpty(c.name))
                    .OrderBy(c => c.name)
                    .ToList();

                return Ok(countries);
            }
            catch (Exception)
            {
                return Ok(GetLocalCountries());
            }
        }

        [HttpGet("provinces")]
        public async Task<IActionResult> GetProvinces([FromQuery] string country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return BadRequest(new { message = "Query parameter 'country' is required." });

            try
            {
                var client = _httpFactory.CreateClient("CountriesNow");
                var payload = JsonSerializer.Serialize(new { country });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("countries/states", content);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode(502, new { message = $"Upstream API error for country '{country}'.", detail = json });

                var doc = JsonSerializer.Deserialize<JsonElement>(json);

                if (doc.TryGetProperty("error", out var err) && err.GetBoolean())
                    return NotFound(new { message = $"Country '{country}' not found in location database." });

                var states = doc
                    .GetProperty("data")
                    .GetProperty("states")
                    .EnumerateArray()
                    .Select(s => new
                    {
                        name = s.GetProperty("name").GetString(),
                        stateCode = s.TryGetProperty("state_code", out var sc) ? sc.GetString() : null
                    })
                    .Where(s => s.name != null)
                    .OrderBy(s => s.name)
                    .ToList();

                return Ok(states);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching provinces.", error = ex.Message });
            }
        }

        [HttpGet("cities")]
        public async Task<IActionResult> GetCities([FromQuery] string country, [FromQuery] string state)
        {
            if (string.IsNullOrWhiteSpace(country))
                return BadRequest(new { message = "Query parameter 'country' is required." });

            if (string.IsNullOrWhiteSpace(state))
                return BadRequest(new { message = "Query parameter 'state' is required." });

            try
            {
                var client = _httpFactory.CreateClient("CountriesNow");
                var payload = JsonSerializer.Serialize(new { country, state });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("countries/state/cities", content);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode(502, new { message = $"Upstream API error for state '{state}'.", detail = json });

                var doc = JsonSerializer.Deserialize<JsonElement>(json);

                if (doc.TryGetProperty("error", out var err) && err.GetBoolean())
                    return NotFound(new { message = $"State '{state}' not found in '{country}'." });

                var cities = doc
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(c => c.GetString())
                    .Where(c => c != null)
                    .OrderBy(c => c)
                    .ToList();

                return Ok(cities);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching cities.", error = ex.Message });
            }
        }
        [HttpGet("full")]
        public async Task<IActionResult> GetFull([FromQuery] string country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return BadRequest(new { message = "Query parameter 'country' is required." });

            try
            {
                var client = _httpFactory.CreateClient("CountriesNow");
                var payload = JsonSerializer.Serialize(new { country });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("countries/state/cities", content);

                var resp2 = await client.PostAsync("countries/states", new StringContent(payload, Encoding.UTF8, "application/json"));
                var json2 = await resp2.Content.ReadAsStringAsync();

                if (!resp2.IsSuccessStatusCode)
                    return StatusCode(502, new { message = "Upstream API error." });

                var doc = JsonSerializer.Deserialize<JsonElement>(json2);

                if (doc.TryGetProperty("error", out var err) && err.GetBoolean())
                    return NotFound(new { message = $"Country '{country}' not found." });

                var states = doc
                    .GetProperty("data")
                    .GetProperty("states")
                    .EnumerateArray()
                    .Select(s => new
                    {
                        name = s.GetProperty("name").GetString(),
                        stateCode = s.TryGetProperty("state_code", out var sc) ? sc.GetString() : null
                    })
                    .Where(s => s.name != null)
                    .OrderBy(s => s.name)
                    .ToList();

                return Ok(new { country, states });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching full location data.", error = ex.Message });
            }
        }

        [HttpGet("countries/search")]
        public async Task<IActionResult> SearchCountries([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query parameter 'q' is required." });

            try
            {
                var client = _httpFactory.CreateClient("CountryApi");
                var resp = await client.GetAsync($"name/{Uri.EscapeDataString(q)}?fields=name,cca2,flags");

                if (!resp.IsSuccessStatusCode)
                    return Ok(Array.Empty<object>());

                var json = await resp.Content.ReadAsStringAsync();

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var rawArray = new List<JsonElement>();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    rawArray = root.EnumerateArray().ToList();
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    rawArray.Add(root);
                }

                if (!rawArray.Any()) return Ok(Array.Empty<object>());

                var results = rawArray.Take(10).Select(c => new
                {
                    name = c.GetProperty("name").GetProperty("common").GetString(),
                    code = c.TryGetProperty("cca2", out var cca2) ? cca2.GetString() : null,
                    flagUrl = c.TryGetProperty("flags", out var f)
                              ? f.TryGetProperty("svg", out var svg) ? svg.GetString() : null
                              : null
                }).ToList();

                return Ok(results);
            }
            catch
            {
                return Ok(Array.Empty<object>());
            }
        }
    }
}
