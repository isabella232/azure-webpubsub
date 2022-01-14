using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.WebPubSub;
using Microsoft.Azure.WebPubSub.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SimpleChat
{
    public static class Functions
    {
        [FunctionName("index")]
        public static IActionResult Home([HttpTrigger(AuthorizationLevel.Anonymous)] HttpRequest req, ILogger log)
        {
            string indexFile = "index.html";
            // detect Azure env.
            if (Environment.GetEnvironmentVariable("HOME") != null)
            {
                indexFile = Path.Join(Environment.GetEnvironmentVariable("HOME"), "site", "wwwroot", indexFile);
            }
            log.LogInformation($"index.html path: {indexFile}.");
            return new ContentResult
            {
                Content = File.ReadAllText(indexFile),
                ContentType = "text/html",
            };
        }

        [FunctionName("login")]
        public static WebPubSubConnection GetClientConnection(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req,
            [WebPubSubConnection(UserId = "{query.userid}", Hub = "simplechat")] WebPubSubConnection connection)
        {
            Console.WriteLine("login");
            return connection;
        }

        #region Work with WebPubSubTrigger
        [FunctionName("connect")]
        public static WebPubSubEventResponse Connect(
            [WebPubSubTrigger("simplechat", WebPubSubEventType.System, "connect")] ConnectEventRequest request)
        {
            Console.WriteLine($"Received client connect with connectionId: {request.ConnectionContext.ConnectionId}");
            if (request.ConnectionContext.UserId == "attacker")
            {
                return request.CreateErrorResponse(WebPubSubErrorCode.Unauthorized, null);
            }
            return request.CreateResponse(request.ConnectionContext.UserId, null, null, null);
        }

        // multi tasks sample
        [FunctionName("connected")]
        public static async Task Connected(
            [WebPubSubTrigger(WebPubSubEventType.System, "connected")] WebPubSubConnectionContext connectionContext,
            [WebPubSub] IAsyncCollector<WebPubSubAction> actions)
        {
            await actions.AddAsync(new SendToAllAction
            {
                Data = BinaryData.FromString(new ClientContent($"{connectionContext.UserId} connected.").ToString()),
                DataType = WebPubSubDataType.Json
            });

            await actions.AddAsync(WebPubSubAction.CreateAddUserToGroupAction(connectionContext.UserId, "group1"));
            await actions.AddAsync(new SendToUserAction
            {
                UserId = connectionContext.UserId,
                Data = BinaryData.FromString(new ClientContent($"{connectionContext.UserId} joined group: group1.").ToString()),
                DataType = WebPubSubDataType.Json
            });
        }

        // single message sample
        [FunctionName("broadcast")]
        public static async Task<WebPubSubEventResponse> Broadcast(
            [WebPubSubTrigger("%WebPubSubHub%", WebPubSubEventType.User, "message")] // another way to resolve Hub name from settings.
            UserEventRequest request,
            WebPubSubConnectionContext connectionContext,
            BinaryData data,
            WebPubSubDataType dataType,
            [WebPubSub(Hub = "simplechat")] IAsyncCollector<WebPubSubAction> actions)
        {
            await actions.AddAsync(new SendToAllAction
            {
                Data = request.Data,
                DataType = request.DataType
            });

            return request.CreateResponse(BinaryData.FromString(new ClientContent("ack").ToString()), WebPubSubDataType.Json);
        }

        [FunctionName("disconnect")]
        [return: WebPubSub(Hub = "%WebPubSubHub%")]
        public static WebPubSubAction Disconnect(
            [WebPubSubTrigger("simplechat", WebPubSubEventType.System, "disconnected")] WebPubSubConnectionContext connectionContext)
        {
            Console.WriteLine("Disconnect.");
            return new SendToAllAction
            {
                Data = BinaryData.FromString(new ClientContent($"{connectionContext.UserId} disconnect.").ToString()),
                DataType = WebPubSubDataType.Text
            };
        }

        #endregion

        [JsonObject]
        public sealed class ClientContent
        {
            [JsonProperty("from")]
            public string From { get; set; }
            [JsonProperty("content")]
            public string Content { get; set; }

            public ClientContent(string message)
            {
                From = "[System]";
                Content = message;
            }

            public ClientContent(string from, string message)
            {
                From = from;
                Content = message;
            }

            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
        }
    }
}
