using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualStreamPlayer.Streaming
{
    public class StreamOpenResult
    {
        public required IcyMetadataStream AudioStream { get; init; }
        public string? StationName { get; init; }
        public string? ContentType { get; init; }
    }

    /// <summary>
    /// Opens an HTTP/ICY streaming URL (Shoutcast/Icecast style) and hands back a
    /// clean audio byte stream, requesting inline metadata so the player can show
    /// "now playing" info without a second connection.
    /// </summary>
    public class StreamClient
    {
        private readonly HttpClient _http;

        public StreamClient()
        {
            _http = new HttpClient();
            _http.Timeout = Timeout.InfiniteTimeSpan; // it's a live stream, not a request/response
        }

        public async Task<StreamOpenResult> OpenAsync(string url, CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            request.Headers.TryAddWithoutValidation("User-Agent", "VirtualStreamPlayer/1.0");

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            int metaInt = 0;
            if (response.Headers.TryGetValues("icy-metaint", out var metaIntValues))
                int.TryParse(System.Linq.Enumerable.FirstOrDefault(metaIntValues), out metaInt);

            string? stationName = null;
            if (response.Headers.TryGetValues("icy-name", out var nameValues))
                stationName = System.Linq.Enumerable.FirstOrDefault(nameValues);

            var rawStream = await response.Content.ReadAsStreamAsync(ct);
            var icyStream = new IcyMetadataStream(rawStream, metaInt);

            return new StreamOpenResult
            {
                AudioStream = icyStream,
                StationName = stationName,
                ContentType = response.Content.Headers.ContentType?.MediaType
            };
        }
    }
}
