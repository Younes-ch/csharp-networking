using System.Net;
using System.Text;

var httpServer = new HttpServer("http://localhost:8000/");
var listenTask = httpServer.HandleIncomingConnections();
listenTask.GetAwaiter().GetResult();

internal class HttpServer
{
    private const string PageData = """
                                        <!DOCTYPE>
                                        <html>
                                            <head>
                                                <title>HttpListener Example</title>
                                            </head>
                                            <body>
                                                <p> Page Views: {0}</p>
                                                <form method="post" action="shutdown">
                                                    <input type="submit" value="Shutdown" {1} />
                                                </form>
                                            </body>
                                        </html>
                                    """;

    private static int _pageViews;
    private static int _requestCount;
    private readonly HttpListener _listener = new();

    public HttpServer(string url)
    {
        _listener.Prefixes.Add(url);
        _listener.Start();
        Console.WriteLine($"Listening for connections on {url}");
    }

    ~HttpServer()
    {
        _listener.Close();
    }

    public async Task HandleIncomingConnections()
    {
        var runServer = true;

        while (runServer)
        {
            var ctx = await _listener.GetContextAsync();

            var request = ctx.Request;
            var response = ctx.Response;

            Console.WriteLine($"Request: #: {++_requestCount}");
            Console.WriteLine(request.Url?.ToString());
            Console.WriteLine(request.HttpMethod);
            Console.WriteLine(request.UserHostName);
            Console.WriteLine(request.UserAgent);
            Console.WriteLine();

            if (request is { HttpMethod: "POST", Url.AbsolutePath: "/shutdown" })
            {
                Console.WriteLine("Shutdown requested.");
                runServer = false;
            }

            if (request.Url?.AbsolutePath != "/favicon.ico")
                _pageViews++;

            var disableSubmit = !runServer ? "disabled" : "";
            var data = Encoding.UTF8.GetBytes(string.Format(PageData, _pageViews, disableSubmit));
            response.ContentType = "text/html";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = data.Length;

            await response.OutputStream.WriteAsync(data);
            response.Close();

            /*
             * With how the code is currently structured inside the loop, only one client can be handled at a time.
             * So if a second client tries to connect while the server is still talking to another user, it will have to
             * wait until the server is done with the first client.
             * A common pattern in server design is to create a new thread or fork the server process immediately after
             * a user has connected to it.
             * This way it can handle new incoming connections while serving that user.
             * Uncomment the following line and then try viewing the page a few times.
             */

            // Thread.Sleep(TimeSpan.FromSeconds(10));
        }
    }
}