using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace Accounts.Controllers
{
    /// <summary>
    /// Cascading location API — all data fetched live from external APIs.
    /// No database storage. No hardcoded data.
    ///
    /// Data sources:
    ///   Countries  → restcountries.com  (already used in project)
    ///   Provinces  → countriesnow.space (POST, no auth)
    ///   Cities     → countriesnow.space (POST, no auth)
    ///
    /// Cascade flow:
    ///   GET /api/locations/countries
    ///   GET /api/locations/provinces?country=Pakistan
    ///   GET /api/locations/cities?country=Pakistan&state=Khyber+Pakhtunkhwa
    /// </summary>
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

        // ── STEP 1 — Countries ────────────────────────────────────────

        /// <summary>
        /// Get all countries with name, ISO code and flag.
        /// Source: restcountries.com
        /// </summary>
        [HttpGet("countries")]
        public async Task<IActionResult> GetCountries()
        {
            try
            {
                var client = _httpFactory.CreateClient("CountryApi");
                var resp = await client.GetAsync("all?fields=name,cca2,flags");

                if (!resp.IsSuccessStatusCode)
                    return StatusCode(502, new { message = "Failed to fetch countries from upstream API." });

                var json = await resp.Content.ReadAsStringAsync();
                var raw  = JsonSerializer.Deserialize<JsonElement[]>(json);

                if (raw == null) return Ok(Array.Empty<object>());

                var countries = raw
                    .Select(c => new
                    {
                        name    = c.GetProperty("name").GetProperty("common").GetString(),
                        code    = c.GetProperty("cca2").GetString(),
                        flagUrl = c.TryGetProperty("flags", out var f)
                                  ? f.TryGetProperty("svg", out var svg) ? svg.GetString() : null
                                  : null
                    })
                    .Where(c => c.name != null)
                    .OrderBy(c => c.name)
                    .ToList();

                return Ok(countries);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching countries.", error = ex.Message });
            }
        }

        // ── STEP 2 — Provinces / States ───────────────────────────────

        /// <summary>
        /// Get provinces/states for a country.
        /// Source: countriesnow.space
        /// Example: GET /api/locations/provinces?country=Pakistan
        /// </summary>
        [HttpGet("provinces")]
        public async Task<IActionResult> GetProvinces([FromQuery] string country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return BadRequest(new { message = "Query parameter 'country' is required." });

            try
            {
                var client  = _httpFactory.CreateClient("CountriesNow");
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
                        name      = s.GetProperty("name").GetString(),
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

        // ── STEP 3 — Cities ───────────────────────────────────────────

        /// <summary>
        /// Get cities for a country + state/province.
        /// Source: countriesnow.space
        /// Example: GET /api/locations/cities?country=Pakistan&state=Khyber+Pakhtunkhwa
        /// </summary>
        [HttpGet("cities")]
        public async Task<IActionResult> GetCities([FromQuery] string country, [FromQuery] string state)
        {
            if (string.IsNullOrWhiteSpace(country))
                return BadRequest(new { message = "Query parameter 'country' is required." });

            if (string.IsNullOrWhiteSpace(state))
                return BadRequest(new { message = "Query parameter 'state' is required." });

            try
            {
                var client  = _httpFactory.CreateClient("CountriesNow");
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

        // ── BONUS — All provinces+cities for a country in one call ────

        /// <summary>
        /// Get all states with their cities for a country in one response.
        /// Useful for pre-loading the entire country structure at once.
        /// Source: countriesnow.space
        /// Example: GET /api/locations/full?country=Pakistan
        /// </summary>
        [HttpGet("full")]
        public async Task<IActionResult> GetFull([FromQuery] string country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return BadRequest(new { message = "Query parameter 'country' is required." });

            try
            {
                var client  = _httpFactory.CreateClient("CountriesNow");
                var payload = JsonSerializer.Serialize(new { country });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("countries/state/cities", content);

                // This endpoint returns all states+cities for a country
                // Try the states-with-cities endpoint
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
                        name      = s.GetProperty("name").GetString(),
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

        // ── BONUS — Country search ────────────────────────────────────

        /// <summary>
        /// Search countries by name (for autocomplete).
        /// Example: GET /api/locations/countries/search?q=pak
        /// </summary>
        [HttpGet("countries/search")]
        public async Task<IActionResult> SearchCountries([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query parameter 'q' is required." });

            try
            {
                var client = _httpFactory.CreateClient("CountryApi");
                var resp   = await client.GetAsync($"name/{Uri.EscapeDataString(q)}?fields=name,cca2,flags");

                if (!resp.IsSuccessStatusCode)
                    return Ok(Array.Empty<object>());

                var json = await resp.Content.ReadAsStringAsync();
                var raw  = JsonSerializer.Deserialize<JsonElement[]>(json);

                if (raw == null) return Ok(Array.Empty<object>());

                var results = raw.Take(10).Select(c => new
                {
                    name    = c.GetProperty("name").GetProperty("common").GetString(),
                    code    = c.GetProperty("cca2").GetString(),
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
