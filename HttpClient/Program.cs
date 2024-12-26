const string urlToDownload = "https://example.com";
const string fileName = "index.html";

var webPageDownloaderService = new WebPageDownloaderService(urlToDownload, fileName);
var downloadTask = webPageDownloaderService.DownloadWebPage();
Console.WriteLine("Holding for at least 5 seconds...");
Thread.Sleep(TimeSpan.FromSeconds(5));

downloadTask.GetAwaiter().GetResult();

internal class WebPageDownloaderService(string url, string fileName)
{
    public async Task DownloadWebPage()
    {
        Console.WriteLine("Starting download...");

        using var httpClient = new HttpClient();

        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine("Fetched the webpage...");

            var data = await response.Content.ReadAsByteArrayAsync();

            var fileStream = File.Create(fileName);
            await fileStream.WriteAsync(data);
            fileStream.Close();

            Console.WriteLine("Done!");
        }
    }
}