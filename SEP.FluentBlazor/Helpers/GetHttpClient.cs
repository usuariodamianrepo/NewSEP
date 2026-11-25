namespace SEP.FluentBlazor.Helpers
{
    public class GetHttpClient
    {
        private readonly IHttpClientFactory httpClientFactory;

        public GetHttpClient(IHttpClientFactory httpClientFactory)
        {
            this.httpClientFactory = httpClientFactory;
        }

        public HttpClient GetPublicHttpClient()
        {
            var client = httpClientFactory.CreateClient("SystemApiClient");
            return client;
        }
    }
}
