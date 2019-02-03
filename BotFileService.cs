using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Collections.Generic;

namespace GameATron4000Assistant
{
    public static class BotFileService
    {
        private static HttpClient HttpClient = new HttpClient();

        [FunctionName("HttpTrigger")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var environments = Environment.GetEnvironmentVariable("GAMEATRON_ENVIRONMENTS").Split(';');
            var storageConnectionString = Environment.GetEnvironmentVariable("GAMEATRON_STORAGE_CONNECTION_STRING");
            var armClientId = Environment.GetEnvironmentVariable("GAMEATRON_ARM_CLIENT_ID");
            var armClientSecret = Environment.GetEnvironmentVariable("GAMEATRON_ARM_CLIENT_SECRET");
            var tenantId = Environment.GetEnvironmentVariable("GAMEATRON_TENANT_ID");
            var subscriptionId = Environment.GetEnvironmentVariable("GAMEATRON_SUBSCRIPTION_ID");
            var appPassword = Environment.GetEnvironmentVariable("GAMEATRON_APP_PASSWORD");

            string environment = req.Query["env"];
            string instance = req.Query["instance"];
            string endpoint = req.Query["endpoint"];

            if (!environments.Contains(environment)
                ||string.IsNullOrWhiteSpace(instance)
                ||string.IsNullOrWhiteSpace(endpoint))
            {
                return new Microsoft.AspNetCore.Mvc.StatusCodeResult(403);
            }

            var resourceGroup = $"GameATron4000Environment-{environment}";
            var botName = $"GameATron4000-{environment}-{instance}";

            var accessToken = await GetAccessTokenAsync(tenantId, armClientId, armClientSecret);

            // Get the pre-registered bot service resource.
            var botService = await GetResourceAsync(
                accessToken,
                subscriptionId,
                resourceGroup,
                $"Microsoft.BotService/botServices/{botName}");

            // Update the endpoint in the resource.
            botService["properties"]["endpoint"] = endpoint;
            //
            await UpdateResourceAsync(
                accessToken,
                subscriptionId,
                resourceGroup,
                $"Microsoft.BotService/botServices/{botName}",
                botService);

            // Get the direct line secret for this bot service.
            var directLineChannel = await GetResourceAsync(
                accessToken,
                subscriptionId,
                resourceGroup,
                $"Microsoft.BotService/botServices/{botName}/channels/DirectLineChannel/listChannelWithKeys");
            //
            var directLineSecret = directLineChannel["properties"]["properties"]["sites"].First()["key"].Value<string>();

            // Create a bot file containing all the secrets.
            var botFile = CreateBotFile(
                endpoint,
                botService["properties"]["msaAppId"].Value<string>(),
                appPassword,
                directLineSecret,
                botName,
                tenantId,
                subscriptionId,
                resourceGroup);

            // And upload it to the storage account for the user to download.
            var account = CloudStorageAccount.Parse(storageConnectionString);
            var blobClient = account.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference("botfiles");
            var blob = blobContainer.GetBlockBlobReference($"{environment}/{Guid.NewGuid()}/GameATron4000.Development.bot");
            //
            await blob.UploadTextAsync(botFile.ToString());

            return new OkObjectResult(blob.Uri);
        }

        private static async Task<string> GetAccessTokenAsync(string tenantId, string clientId, string clientSecret)
        {
            var requestUrl = $"https://login.windows.net/{tenantId}/oauth2/token";
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    [ "resource" ] = "https://management.core.windows.net/",
                    [ "client_id" ] = clientId,
                    [ "client_secret" ] = clientSecret,
                    [ "grant_type" ] = "client_credentials"
                })
            };

            request.Headers.Add("Accept", "application/json");

            var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var responseBody = JObject.Parse(await response.Content.ReadAsStringAsync());

            return responseBody["access_token"].Value<string>();
        }

        private static async Task<JObject> GetResourceAsync(string accessToken, string subscriptionId,
            string resourceGroup, string resource)
        {
            var requestUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/{resource}?api-version=2018-07-12";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>())
            };

            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        private static async Task UpdateResourceAsync(string accessToken, string subscriptionId,
            string resourceGroup, string resource, JObject resourceBody)
        {
            var requestUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/{resource}?api-version=2018-07-12";
            var request = new HttpRequestMessage(HttpMethod.Patch, requestUrl)
            {
                Content = new StringContent(resourceBody.ToString(), Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private static JObject CreateBotFile(string endpoint, string appId,
            string appPassword, string directLineSecret, string botName, string tenantId,
            string subscriptionId, string resourceGroup)
        {
            return JObject.FromObject(new
            {
                name = "GameATron4000",
                description = "",
                services = new object[]
                {
                    new
                    {
                        type = "endpoint",
                        name = "GameATron4000",
                        endpoint = endpoint,
                        appId = appId,
                        appPassword = appPassword,
                        id = "1"
                    },
                    new
                    {
                        type = "generic",
                        name = "DirectLine",
                        url = "nourl",
                        configuration = new
                        {
                            secret = directLineSecret
                        },
                        id = "2"
                    },
                    new
                    {
                        type = "abs",
                        name = "bot",
                        serviceName = botName,
                        tenantId = tenantId,
                        subscriptionId = subscriptionId,
                        resourceGroup = resourceGroup,
                        appId = appId,
                        id = "3"
                    }
                },
                padlock = "",
                version = "2.0"
            });
        }
    }
}
